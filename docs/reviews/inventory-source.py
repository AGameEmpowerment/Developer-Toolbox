"""Dated source inventory; extends docs/architecture/inventory-source.py.

The paired QA Stack Docs/testing tool shares the counting algorithm. Historical
architecture inventory files retain their original reproducer and source date.
"""
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
DATE='2026-10-22'

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

def inventory(root,slug,source_commit=None,check=False,output_dir=None,worktree=False):
    try:
        script_reference=Path(__file__).resolve().relative_to(root.resolve()).as_posix()
    except ValueError:
        script_reference=str(Path(__file__).resolve())
    commit=git(root,'rev-parse',source_commit or 'HEAD')
    files=git(root,'ls-tree','-r','--name-only',commit).splitlines()
    if worktree:
        files=sorted(name for name in set(git(root,'ls-files','--cached','--others','--exclude-standard').splitlines())
                     if (root/name).is_file())
    inputs={name for name in files if Path(name).suffix in ('.cs','.csproj','.java','.py','.js','.jsx','.ts','.tsx','.mjs','.sh','.ps1')}
    changed=set(git(root,'diff','--name-only',commit).splitlines())
    if not worktree and inputs.intersection(changed):
        raise SystemExit('Source differs from selected source commit; select and review the current source first.')
    def read_sources(names):
        return {name:(root/name).read_bytes() for name in names} if worktree else git_blobs(root,commit,names)
    source_files=[name for name in files if Path(name).suffix in ('.cs','.java','.py','.mjs','.jsx','.js','.ts','.tsx','.sh','.ps1')]
    project_frameworks={}
    for name,raw in read_sources([name for name in files if name.endswith('.csproj')]).items():
        packages={item.attrib.get('Include','').lower() for item in ET.fromstring(raw).iter('PackageReference')}
        if 'nunit' in packages or 'xunit' in packages:
            project_frameworks[Path(name).parent]='NUnit' if 'nunit' in packages else 'xUnit'
    records=[]
    for name,raw in read_sources(source_files).items():
        p=Path(name)
        framework=None; counts={}; definitions=[]
        text=raw.decode('utf-8-sig',errors='replace')
        if p.suffix=='.cs':
            # Resolve global usings and ambiguous Theory markers from the closest project, never the repository name.
            selected=next((project_frameworks[parent] for parent in p.parents if parent in project_frameworks),None)
            if selected is None:
                selected='NUnit' if re.search(r'NUnit\.Framework|\[\s*(?:Test|TestCase|TestCaseSource)\b',text) else 'xUnit'
            # One record per authored test attribute. No adapter, source expansion or fixture instantiation.
            for fw,pattern in [('NUnit',r'(?:(?:^|(?<=\]))\s*\[|,)\s*(?:NUnit\.Framework\.)?(Test|TestCase|TestCaseSource|Theory)(?=\s*[\],(])'),('xUnit',r'(?:(?:^|(?<=\]))\s*\[|,)\s*(?:Xunit\.)?(Fact|Theory)(?=\s*[\],(])')]:
                matches=re.findall(pattern,text,re.M)
                if matches and fw==selected:
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
            markers=list(re.finditer(r'^\s*@(?:org\.junit\.jupiter\.(?:api|params)\.)?(?:Test|ParameterizedTest|RepeatedTest)\b',text,re.M))
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
        elif p.suffix in ('.sh','.ps1') and p.name.lower().startswith(('test_', 'test-')):
            framework='Standalone shell scripts'; counts={'scripts':1}
        if framework and any(counts.values()):
            area=name.split('/')[0]
            if area in ('src','tests') and len(name.split('/'))>2: area='/'.join(name.split('/')[:2])
            if name.startswith('src/DeveloperToolbox.Example/'): area='/'.join(name.split('/')[:3])
            if name.startswith('src/DeveloperToolbox.Java.Example/'): area='/'.join(name.split('/')[:3])
            records.append({'source':name,'sha256':hashlib.sha256(raw).hexdigest(),'framework':framework,'area':area,'counts':counts,'definitions':definitions})
    data={'date':DATE,'sourceCommit':commit,'repository':slug,'method':'Selected-commit Git-blob lexical declaration inventory; Python functions use AST. SHA-256 hashes use exact Git blob bytes, independent of checkout line endings. Counts are source markers, not adapter-discovered cases. See inventory-source.py for exact expressions. Comments/generated strings can require review; no parameter/source/fixture/browser/role expansion.', 'executed':'Not collected by this source-only inventory; execution evidence is reported separately','coverage':'Unavailable: no measured coverage report generated or imported','files':records}
    out=root/(output_dir or 'Docs/testing')
    if worktree:
        data['sourceState']='Uncommitted working tree based on sourceCommit; not a committed baseline'
        data['method']='Working-tree lexical declaration inventory; Python functions use AST. SHA-256 hashes use exact checkout bytes. No parameter, fixture, browser or role expansion. Source markers are not discovered cases or execution results.'
        data['sourceManifest']={name:hashlib.sha256(raw).hexdigest() for name,raw in read_sources(sorted(inputs)).items()}
    payload=json.dumps(data,indent=2)+'\n'
    group=collections.defaultdict(collections.Counter)
    for r in records: group[(r['framework'],r['area'])].update(r['counts'])
    md=front('Test inventory','Verifiable source test markers separated from discovery and execution.')
    md+=f'## Scope and method\n\nReviewed {DATE}, exact source commit `{commit}`. Repository `{slug}`.\n\n'
    md+='This is a tracked-source declaration inventory, not a runtime test total. NUnit attributes are separated into `[Test]`, inline `[TestCase]`, source `[TestCaseSource]` and `[Theory]` markers; multiple inline rows can belong to one authored method. The separate authoredAttributedMethodDeclarations total maps each marker to the next C# method and deduplicates by source line; per-file method names and lines are retained in JSON. This is a syntax scan, not Roslyn or framework discovery. xUnit Fact/Theory and JUnit annotations are source markers, with deduplicated authored method declarations listed separately. Chained and comma-separated C# attributes are included. Python counts AST test functions. JavaScript counts literal test call sites; parameterized sites are a subset and must not be added to them. Data rows and repeated helper registration are not expanded. node:test imports identify the separate Node runner. Standalone PowerShell/Bash contract scripts are a separate area.\n\n'
    md+='This source-only inventory does not execute adapter discovery or application tests and does not import execution results. Consult separately linked execution evidence for discovered cases and pass/fail/skip totals at the selected commit. Repeated browsers, personas, roles, fixtures and parameter sources are not expanded; snapshots are not counted as tests. Code coverage is unavailable without a measured report. Test counts do not establish assertion quality.\n\n'
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
        'and rejects changed source inputs. The recorded commit pins the reviewed source snapshot, independently '
        'of later inventory-tooling changes. Without `--check`, it regenerates the two inventory documents. '
        '`--check` validates both reports without rewriting them.\n\n'
        f'```powershell\npython {script_reference} --root . --repo {slug} --output-dir {output_dir} --date {DATE} --source-commit {commit} --check\n```\n\n'
    )
    md+='Full source scan excludes untracked build output, dependencies and Git submodule contents. Upstream vendored dependencies are not silently added to downstream test totals. Existing historical discovery and execution evidence remains separate from this current source inventory.\n'
    if worktree:
        md=md.replace('exact source commit', 'working tree based on source commit')
        md=md.replace('tracked-source declaration inventory', 'working-tree declaration inventory')
        md=md.replace('SHA-256 of its Git blob', 'SHA-256 of its checkout bytes')
        start=md.index('The adjacent `test-inventory.json`')
        end=md.index('```powershell',start)
        md=md[:start]+('The adjacent JSON records per-file counts and checkout hashes, plus a manifest of all scanned source inputs. '
                      'The source commit identifies the base of this uncommitted working tree, not a committed version of these changes. '
                      'Repeat with --worktree while reviewing the change set.\n\n')+md[end:]
        md=md.replace(' --check\n```', ' --worktree --check\n```')
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
    parser.add_argument('--worktree',action='store_true',help='Include uncommitted and untracked source with explicit checkout hashes.')
    parser.add_argument('--root',type=Path,default=Path(__file__).resolve().parents[2])
    parser.add_argument('--repo',default='AGameEmpowerment/Developer-Toolbox')
    parser.add_argument('--output-dir',default='docs/reviews/test-inventory-2026-10-22')
    parser.add_argument('--date',default=DATE)
    args=parser.parse_args()
    DATE=args.date
    inventory(args.root,args.repo,args.source_commit,args.check,args.output_dir,args.worktree)
