import ast
import collections
import hashlib
import io
import json
import re
import subprocess
import textwrap
from pathlib import Path
import xml.etree.ElementTree as ET

BASE=Path(__file__).resolve().parent
DATE='2026-10-20'

def git(root,*args):
    return subprocess.check_output(['git','-C',str(root),*args],text=True,encoding='utf-8').strip()

def git_blobs(root,commit,files):
    requests=''.join(f'{commit}:{name}\n' for name in files).encode('utf-8')
    payload=subprocess.check_output(['git','-C',str(root),'cat-file','--batch'],input=requests)
    stream=io.BytesIO(payload)
    blobs={}
    for name in files:
        header=stream.readline().split()
        if len(header)!=3 or header[1]!=b'blob':
            raise SystemExit('Cannot read source blob: '+name)
        size=int(header[2])
        raw=stream.read(size)
        if len(raw)!=size or stream.read(1)!=b'\n':
            raise SystemExit('Incomplete source blob: '+name)
        blobs[name]=raw
    return blobs

def front(title,summary):
    return f'''---
post_title: {title}
author1: Engineering team
post_slug: {title.lower().replace(' ','-')}
microsoft_alias: n/a
featured_image: n/a
categories: [documentation]
tags: [architecture, testing]
ai_note: AI assisted
summary: {summary}
post_date: {DATE}
---

'''

def inventory(root,slug,source_commit=None,check=False):
    commit=git(root,'rev-parse',source_commit or 'HEAD')
    files=git(root,'ls-tree','-r','--name-only',commit).splitlines()
    inputs={name for name in files if Path(name).suffix in ('.cs','.csproj','.java','.py','.js','.jsx','.ts','.tsx','.mjs','.sh','.ps1')}
    changed=set(git(root,'diff','--name-only',commit).splitlines())
    if inputs.intersection(changed):
        raise SystemExit('Source differs from selected source commit; select and review the current source first.')
    source_files=[name for name in files if Path(name).suffix in ('.cs','.java','.py','.mjs','.jsx','.js','.ts','.tsx','.sh','.ps1')]
    records=[]
    for name,raw in git_blobs(root,commit,source_files).items():
        p=Path(name)
        framework=None; counts={}; definitions=[]
        text=raw.decode('utf-8-sig',errors='replace')
        if p.suffix=='.cs':
            # One record per authored test attribute. No adapter, source expansion or fixture instantiation.
            for fw,pattern in [('NUnit',r'(?:(?:^|(?<=\]))\s*\[|,)\s*(Test|TestCase|TestCaseSource|Theory)(?=\s*[\],(])'),('xUnit',r'(?:(?:^|(?<=\]))\s*\[|,)\s*(Fact|Theory)(?=\s*[\],(])')]:
                matches=re.findall(pattern,text,re.M)
                if matches and (fw=='NUnit' and not slug.endswith('Toolbox') or fw=='xUnit' and slug.endswith('Toolbox')):
                    framework=fw; counts=dict(collections.Counter(matches))
                    method=re.compile(r'\b(?:public|internal|protected|private)\s+(?:(?:async|static|virtual|override|sealed|new)\s+)*(?:[\w.<>,?\[\]]+\s+)+(?P<name>\w+)\s*(?:<[^>]+>)?\s*\(')
                    unique={}
                    for marker in re.finditer(pattern,text,re.M):
                        found=method.search(text,marker.end())
                        if found is None or found.start()-marker.end()>5000:
                            raise SystemExit('Unmapped test marker: '+name)
                        line=text.count('\n',0,found.start())+1
                        unique[line]={'method':found['name'],'line':line}
                    definitions=list(unique.values())
                    counts['authoredAttributedMethodDeclarations']=len(definitions)
                    break
        elif p.suffix=='.java' and '/src/test/' in name:
            framework='JUnit'
            unique={}
            markers=list(re.finditer(r'^\s*@(?:Test|ParameterizedTest|RepeatedTest)\b',text,re.M))
            for marker in markers:
                found=re.search(r'\bvoid\s+(?P<name>\w+)\s*\(',text[marker.end():])
                if found is None: raise SystemExit('Unmapped JUnit marker: '+name)
                line=text.count('\n',0,marker.end()+found.start())+1
                unique[line]={'method':found['name'],'line':line}
            definitions=list(unique.values())
            counts={'testAnnotations':len(markers),'authoredAttributedMethodDeclarations':len(definitions)}
        elif p.suffix=='.py' and (p.name.startswith('test_') or '/tests/' in name):
            try:
                tree=ast.parse(text)
                n=sum(isinstance(x,(ast.FunctionDef,ast.AsyncFunctionDef)) and x.name.startswith('test_') for x in ast.walk(tree))
                if n: framework='Python test functions'; counts={'authoredFunctions':n}
            except SyntaxError: pass
        elif p.suffix in ('.mjs','.jsx','.js','.ts','.tsx') and (re.search(r'(^|/)(tests?/|[^/]+\.(?:test|spec)\.)',name) or name.endswith('/testHelpers.jsx')):
            n=len(re.findall(r'(?<![\w.])(?:it|test)(?:\.(?:skip|only|todo|each))?\s*\(',text))
            if n: framework='Node test' if 'node:test' in text else 'Vitest'; counts={'authoredCallSites':n,'parameterizedCallSites':len(re.findall(r'\b(?:it|test)\.each\b',text))}
        elif p.suffix in ('.sh','.ps1') and (p.name.startswith('test_') or p.name.startswith('test-')):
            framework='Standalone shell scripts'; counts={'scripts':1}
        if framework and any(counts.values()):
            area=name.split('/')[0]
            if area in ('src','tests') and len(name.split('/'))>2: area='/'.join(name.split('/')[:2])
            if name.startswith('src/DeveloperToolbox.Example/'): area='/'.join(name.split('/')[:3])
            if name.startswith('src/DeveloperToolbox.Java.Example/'): area='/'.join(name.split('/')[:3])
            records.append({'source':name,'sha256':hashlib.sha256(raw).hexdigest(),'framework':framework,'area':area,'counts':counts,'definitions':definitions})
    data={'date':DATE,'sourceCommit':commit,'repository':slug,'method':'Selected-commit Git-blob lexical declaration inventory; Python functions use AST. SHA-256 hashes use exact Git blob bytes, independent of checkout line endings. Counts are source markers, not adapter-discovered cases. See inventory-source.py for exact expressions. Comments/generated strings can require review; no parameter/source/fixture/browser/role expansion.', 'executed':'Not run for this documentation update','coverage':'Unavailable: no measured coverage report generated or imported','files':records}
    out=root/'docs/architecture'
    payload=json.dumps(data,indent=2)+'\n'
    group=collections.defaultdict(collections.Counter)
    for r in records: group[(r['framework'],r['area'])].update(r['counts'])
    md=front('Test inventory','Verifiable source test markers separated from discovery and execution.')
    md+=f'## Scope and method\n\nReviewed {DATE}, exact source commit `{commit}`. Repository `{slug}`.\n\n'
    md+='This is a tracked-source declaration inventory, not a runtime test total. NUnit attributes are separated into `[Test]`, inline `[TestCase]`, source `[TestCaseSource]` and `[Theory]` markers; multiple inline rows can belong to one authored method. The separate authoredAttributedMethodDeclarations total maps each marker to the next C# method and deduplicates by source line; per-file method names and lines are retained in JSON. This is a syntax scan, not Roslyn or framework discovery. xUnit Fact/Theory and JUnit annotations are source markers, with deduplicated authored method declarations listed separately. Chained and comma-separated C# attributes are included. Python counts AST test functions. JavaScript counts literal test call sites including the shared client testHelpers.jsx definitions; parameterized sites are a subset of these call sites and must not be added to them. Data rows and repeated helper registration are not expanded. Client globals are provided by its Vitest configuration; node:test imports identify the separate Node runner. Standalone PowerShell/Bash contract scripts are a separate area.\n\n'
    md+='No adapter discovery or application tests were run for this documentation update. Discovered cases and pass/fail/skip totals are unavailable for this exact commit. Repeated browsers, personas, roles, fixtures and parameter sources are not expanded; snapshots are not counted as tests. Code coverage is unavailable without a measured report. Test counts do not establish assertion quality.\n\n'
    md+='| Framework | Test area | Authored definitions and separate source markers |\n| --- | --- | --- |\n'
    for (fw,area),counts in sorted(group.items()): md+=f"| {fw} | `{area}` | "+'; '.join(f'{k}: **{v}**' for k,v in sorted(counts.items()))+' |\n'
    totals=collections.defaultdict(collections.Counter)
    for (fw,_),c in group.items(): totals[fw].update(c)
    md+='\n### Repository totals by framework\n\n| Framework | Authored definitions and separate source markers |\n| --- | --- |\n'
    for fw,counts in sorted(totals.items()): md+=f"| {fw} | "+'; '.join(f'{k}: **{v}**' for k,v in sorted(counts.items()))+' |\n'
    md+=(
        '\n## Reproduction and evidence\n\n'
        'The adjacent `test-inventory.json` includes every counted path, marker totals and SHA-256 of its Git blob '
        'at the selected commit. `inventory-source.py` contains the exact counting implementation. Run it from the '
        'repository root; it reads source text and hashes from Git blobs, independently of checkout line endings, '
        'and rejects changed source inputs. The recorded commit pins the reviewed PR-stack snapshot, independently '
        'of later inventory-tooling changes. Without `--check`, it regenerates the two inventory documents. '
        '`--check` validates both reports without rewriting them.\n\n'
        f'```powershell\npython docs/architecture/inventory-source.py --source-commit {commit} --check\n```\n\n'
    )
    md+='Full source scan excludes untracked build output, dependencies and Git submodule contents. Upstream vendored dependencies are not silently added to downstream test totals. Existing historical discovery and execution evidence remains separate from this current source inventory.\n'
    md='\n\n'.join(
        paragraph if paragraph.startswith(('---','#','|','```')) else textwrap.fill(
            paragraph,width=120,break_long_words=False,break_on_hyphens=False)
        for paragraph in md.split('\n\n')
    ).rstrip()+'\n'
    reports={'test-inventory.json':payload,'test-inventory.md':md}
    if check:
        for name,expected in reports.items():
            path=out/name
            if not path.is_file() or path.read_text(encoding='utf-8') != expected:
                raise SystemExit('Inventory differs: '+name+'; regenerate and review.')
        print('Inventory matches:',slug,commit,len(records),'counted files')
        return commit,files
    out.mkdir(parents=True,exist_ok=True)
    for name,content in reports.items():
        (out/name).write_text(content,encoding='utf-8')
    return commit,files


if __name__=='__main__':
    import argparse
    parser=argparse.ArgumentParser()
    parser.add_argument('--source-commit',required=True)
    parser.add_argument('--check',action='store_true')
    args=parser.parse_args()
    root=Path(__file__).resolve().parents[2]
    inventory(root,'AGameEmpowerment/Developer-Toolbox',args.source_commit,args.check)
