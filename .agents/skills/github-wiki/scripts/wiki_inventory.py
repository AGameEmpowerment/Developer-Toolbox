#!/usr/bin/env python3
"""Inventory a GitHub repository and its wiki for the github-wiki skill.

Mirrors wiki_inventory.ps1: same checks, same sections, same JSON shape.

Usage:
  python wiki_inventory.py [--repo PATH] [--wiki-dir PATH] [--depth N] [--no-clone] [--json]

Reads repository identity through the GitHub CLI, clones or refreshes the wiki
into a folder outside the project tree, and reports wiki pages (title, size,
last commit, headings, links, broken links, orphans) plus a depth-limited
project tree, documentation-bearing files, and recent commits.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
from collections import Counter
from pathlib import Path

DOC_PATTERNS = [
    r"^(readme|contributing|changelog|license|licence|security|code_of_conduct|support)([.-].*)?$",
    r"^codeowners$",
    r"^(agents|claude|gemini|copilot-instructions)\.md$",
    r"^docs/",
    r"^\.github/",
    r"^\.agents/",
    r"^\.claude(-plugin)?/",
    r"^\.codex-plugin/",
    r"(^|/)plugin\.json$",
    r"(^|/)marketplace\.json$",
    r"(^|/)skill\.md$",
    r"\.(sln|csproj|fsproj|vbproj)$",
    r"(^|/)package\.json$",
    r"(^|/)pyproject\.toml$",
    r"(^|/)requirements[^/]*\.txt$",
    r"(^|/)dockerfile$",
    r"(^|/)docker-compose[^/]*\.ya?ml$",
    r"(^|/)azure-pipelines[^/]*\.ya?ml$",
    r"(^|/)appsettings[^/]*\.json$",
    r"(^|/)\.env\.example$",
    r"(^|/)adr[s]?/",
]
DOC_REGEX = [re.compile(p, re.IGNORECASE) for p in DOC_PATTERNS]
SPECIAL_PAGES = {"home", "_sidebar", "_footer"}
WIKI_LINK = re.compile(r"\[\[([^\]]+)\]\]")
MD_LINK = re.compile(r"(?<!\!)\[[^\]]*\]\(([^)\s]+)(?:\s+\"[^\"]*\")?\)")
IMG_LINK = re.compile(r"!\[[^\]]*\]\(([^)\s]+)")
HEADING = re.compile(r"^(#{1,2})\s+(.*\S)\s*$")


def run(args: list[str], cwd: str | None = None) -> tuple[int, str]:
    try:
        proc = subprocess.run(
            args, cwd=cwd, capture_output=True, text=True, encoding="utf-8", errors="replace"
        )
    except FileNotFoundError:
        return 127, f"{args[0]}: command not found"
    out = (proc.stdout or "") + (proc.stderr or "")
    return proc.returncode, out.strip()


def normalize_target(raw: str) -> str:
    target = raw.strip().split("#", 1)[0]
    if "|" in target:
        target = target.split("|", 1)[1]
    target = target.strip().replace(" ", "-")
    if target.lower().endswith(".md"):
        target = target[:-3]
    return target


def is_external(target: str) -> bool:
    return bool(re.match(r"^(https?:|mailto:|ftp:|//|#)", target, re.IGNORECASE))


def normalize_git_remote(remote: str) -> str:
    value = remote.strip().rstrip("/")
    ssh_prefix = "git@github.com:"
    if value.lower().startswith(ssh_prefix):
        value = "https://github.com/" + value[len(ssh_prefix):]
    if value.lower().endswith(".git"):
        value = value[:-4]
    return value.lower()


def inventory_repo(repo_arg: str, depth: int) -> dict:
    code, root = run(["git", "-C", repo_arg, "rev-parse", "--show-toplevel"])
    if code != 0:
        sys.exit(f"error: {repo_arg} is not inside a git repository ({root})")
    root = os.path.normpath(root)
    info: dict = {"root": root, "folder": os.path.basename(root), "warnings": []}

    _, branch = run(["git", "branch", "--show-current"], root)
    info["branch"] = branch or "(detached)"
    _, head = run(["git", "rev-parse", "--short", "HEAD"], root)
    info["head"] = head
    _, status = run(["git", "status", "--short"], root)
    info["uncommitted_changes"] = len([l for l in status.splitlines() if l.strip()])
    _, remote = run(["git", "remote", "get-url", "origin"], root)
    info["origin"] = remote if remote and not remote.startswith("fatal") else None

    gh_code, gh_out = run(
        ["gh", "repo", "view", "--json",
         "nameWithOwner,url,description,defaultBranchRef,hasWikiEnabled,isPrivate"],
        root,
    )
    if gh_code == 0:
        try:
            gh = json.loads(gh_out)
        except json.JSONDecodeError:
            gh = {}
            info["warnings"].append("gh repo view returned unparseable JSON")
    else:
        gh = {}
        info["warnings"].append(f"gh repo view failed: {gh_out.splitlines()[0] if gh_out else 'unknown error'}")

    url = gh.get("url")
    if not url and info["origin"]:
        m = re.match(r"^(?:git@github\.com:|https://github\.com/)(.+?)(?:\.git)?$", info["origin"])
        if m:
            url = f"https://github.com/{m.group(1)}"
            info["warnings"].append("repository URL derived from the origin remote because gh was unavailable")
    info["name_with_owner"] = gh.get("nameWithOwner") or (url.split("github.com/")[-1] if url else None)
    info["url"] = url
    info["description"] = gh.get("description")
    info["default_branch"] = (gh.get("defaultBranchRef") or {}).get("name")
    info["wiki_enabled"] = gh.get("hasWikiEnabled")
    info["is_private"] = gh.get("isPrivate")
    info["wiki_web_url"] = f"{url}/wiki" if url else None
    info["wiki_git_url"] = f"{url}.wiki.git" if url else None

    if info["default_branch"] and info["branch"] != info["default_branch"]:
        info["warnings"].append(
            f"checkout is on '{info['branch']}', not the default branch '{info['default_branch']}'"
        )
    if info["uncommitted_changes"]:
        info["warnings"].append(f"working tree has {info['uncommitted_changes']} uncommitted change(s)")
    if info["wiki_enabled"] is False:
        info["warnings"].append("the repository wiki is disabled (Settings > Features > Wikis)")

    run(["git", "fetch", "--quiet", "origin"], root)
    if branch:
        code, behind = run(["git", "rev-list", "--count", f"HEAD..origin/{branch}"], root)
        info["behind_remote"] = int(behind) if code == 0 and behind.isdigit() else None
        if info["behind_remote"]:
            info["warnings"].append(f"local branch is {info['behind_remote']} commit(s) behind origin/{branch}")
    else:
        info["behind_remote"] = None

    _, files_out = run(["git", "ls-files"], root)
    files = [f for f in files_out.splitlines() if f]
    info["tracked_files"] = len(files)
    tree: dict = {}
    for f in files:
        parts = f.split("/")
        node = tree
        for part in parts[:-1][:depth]:
            node = node.setdefault(part + "/", {})
        if len(parts) <= depth:
            node.setdefault(parts[-1], None)
        else:
            node["..."] = None
    info["tree"] = tree
    info["doc_files"] = sorted(f for f in files if any(rx.search(f) for rx in DOC_REGEX))
    exts = Counter((Path(f).suffix.lower() or "(none)") for f in files)
    info["extensions"] = exts.most_common(10)
    _, log = run(["git", "log", "-15", "--format=%h %cs %s"], root)
    info["recent_commits"] = log.splitlines()
    return info


def render_tree(node: dict, indent: int = 0) -> list[str]:
    lines = []
    for name in sorted(node, key=lambda n: (not n.endswith("/"), n.lower())):
        lines.append("  " * indent + name)
        if isinstance(node[name], dict):
            lines.extend(render_tree(node[name], indent + 1))
    return lines


def prepare_wiki(info: dict, wiki_dir: str, no_clone: bool) -> dict:
    wiki: dict = {
        "dir": os.path.normpath(os.path.abspath(wiki_dir)),
        "status": None,
        "warnings": [],
        "pages": [],
        "error": False,
    }
    d = wiki["dir"]
    try:
        real_dir = os.path.realpath(d)
        real_root = os.path.realpath(info["root"])
        inside = os.path.commonpath([real_dir, real_root]) == real_root
    except ValueError:  # different drives on Windows
        inside = False
    if inside:
        wiki["warnings"].append("wiki directory is inside the project working tree; choose a sibling or temp folder")
        wiki["status"] = "invalid wiki directory"
        wiki["error"] = True
        return wiki
    if info.get("wiki_enabled") is False:
        wiki["warnings"].append("repository wiki is disabled; clone and refresh were skipped")
        wiki["status"] = "wiki disabled"
        wiki["error"] = True
        return wiki
    is_repo = os.path.exists(os.path.join(d, ".git"))
    if is_repo:
        remote_code, remote = run(["git", "remote", "get-url", "origin"], d)
        expected_remote = info.get("wiki_git_url")
        if remote_code != 0 or not expected_remote:
            wiki["warnings"].append("existing clone could not be verified against the repository wiki")
            wiki["status"] = "unverified existing clone"
            wiki["error"] = True
            return wiki
        if normalize_git_remote(remote) != normalize_git_remote(expected_remote):
            wiki["warnings"].append(f"existing clone remote is {remote}, expected {info['wiki_git_url']}")
            wiki["status"] = "existing clone remote mismatch"
            wiki["error"] = True
            return wiki
        if no_clone:
            wiki["status"] = "existing clone (not refreshed)"
        else:
            code, out = run(["git", "pull", "--ff-only", "--quiet"], d)
            if code == 0:
                wiki["status"] = "refreshed"
            else:
                wiki["status"] = f"pull failed: {out.splitlines()[-1] if out else 'unknown'}"
                wiki["error"] = True
                return wiki
    elif no_clone:
        wiki["status"] = "missing (clone skipped)"
        wiki["warnings"].append("wiki directory does not exist and --no-clone was given")
        wiki["error"] = True
        return wiki
    elif not info["wiki_git_url"]:
        wiki["status"] = "unknown wiki URL"
        wiki["warnings"].append("repository identity did not provide a wiki clone URL")
        wiki["error"] = True
        return wiki
    else:
        code, out = run(["git", "clone", "--quiet", info["wiki_git_url"], d])
        if code != 0:
            wiki["status"] = f"clone failed: {out.splitlines()[-1] if out else 'unknown'}"
            wiki["error"] = True
            if info["wiki_enabled"]:
                wiki["warnings"].append(
                    "wiki is enabled but cannot be cloned; it probably has no pages yet. "
                    f"Create the first page at {info['wiki_web_url']} and retry"
                )
            return wiki
        wiki["status"] = "cloned"

    _, wbranch = run(["git", "branch", "--show-current"], d)
    wiki["branch"] = wbranch
    _, whead = run(["git", "log", "-1", "--format=%h %cs %s"], d)
    wiki["head"] = whead

    page_files = sorted(
        p for p in os.listdir(d)
        if p.lower().endswith((".md", ".markdown")) and os.path.isfile(os.path.join(d, p))
    )
    other = sorted(
        p for p in os.listdir(d)
        if p != ".git" and p not in page_files
    )
    wiki["other_entries"] = other
    names = {Path(p).stem.lower(): Path(p).stem for p in page_files}
    inbound: Counter = Counter()
    pages = []
    for p in page_files:
        path = os.path.join(d, p)
        text = Path(path).read_text(encoding="utf-8", errors="replace")
        stem = Path(p).stem
        _, last = run(["git", "log", "-1", "--format=%cs", "--", p], d)
        headings = [m.group(2) for line in text.splitlines() if (m := HEADING.match(line))]
        links: list[str] = []
        for raw in WIKI_LINK.findall(text):
            links.append(normalize_target(raw))
        for raw in MD_LINK.findall(text):
            if not is_external(raw):
                links.append(normalize_target(raw))
        assets = [a for a in IMG_LINK.findall(text) if not is_external(a)]
        broken = []
        for t in links:
            if not t:
                continue
            key = t.lower()
            if "/" in t and key not in names:
                if not os.path.exists(os.path.join(d, t)):
                    broken.append(t)
                continue
            if key in names:
                if key != stem.lower():
                    inbound[key] += 1
            elif not os.path.exists(os.path.join(d, t)):
                broken.append(t)
        for a in assets:
            if not os.path.exists(os.path.join(d, a.split("#")[0])):
                broken.append(a)
        pages.append({
            "file": p,
            "title": stem.replace("-", " "),
            "bytes": os.path.getsize(path),
            "lines": text.count("\n") + (0 if text.endswith("\n") or not text else 1),
            "last_commit": last or None,
            "headings": headings,
            "links": sorted(set(links)),
            "broken_links": sorted(set(broken)),
        })
    for page in pages:
        stem = Path(page["file"]).stem
        page["inbound_links"] = inbound.get(stem.lower(), 0)
        page["orphan"] = stem.lower() not in SPECIAL_PAGES and page["inbound_links"] == 0
    wiki["pages"] = pages
    wiki["page_count"] = len(pages)
    wiki["broken_link_count"] = sum(len(p["broken_links"]) for p in pages)
    wiki["orphan_count"] = sum(1 for p in pages if p["orphan"])
    return wiki


def render_text(info: dict, wiki: dict, depth: int = 3) -> str:
    out: list[str] = []
    out.append("== Repository ==")
    out.append(f"root:            {info['root']}")
    out.append(f"repository:      {info['name_with_owner'] or '(unknown)'}")
    out.append(f"url:             {info['url'] or '(unknown)'}")
    out.append(f"description:     {info['description'] or '(none)'}")
    out.append(f"branch:          {info['branch']} @ {info['head']}")
    out.append(f"default branch:  {info['default_branch'] or '(unknown)'}")
    out.append(f"uncommitted:     {info['uncommitted_changes']}")
    out.append(f"behind remote:   {info['behind_remote'] if info['behind_remote'] is not None else '(unknown)'}")
    out.append(f"wiki enabled:    {info['wiki_enabled'] if info['wiki_enabled'] is not None else '(unknown)'}")
    out.append(f"wiki url:        {info['wiki_web_url'] or '(unknown)'}")
    out.append(f"wiki git:        {info['wiki_git_url'] or '(unknown)'}")
    for w in info["warnings"]:
        out.append(f"WARNING: {w}")

    out.append("")
    out.append("== Wiki ==")
    out.append(f"clone dir:       {wiki['dir']}")
    out.append(f"status:          {wiki['status']}")
    if wiki.get("branch"):
        out.append(f"wiki branch:     {wiki['branch']}")
        out.append(f"wiki head:       {wiki.get('head')}")
    for w in wiki["warnings"]:
        out.append(f"WARNING: {w}")
    if wiki["pages"]:
        out.append(f"pages: {wiki['page_count']}   broken links: {wiki['broken_link_count']}   orphans: {wiki['orphan_count']}")
        for p in wiki["pages"]:
            flags = []
            if p["orphan"]:
                flags.append("ORPHAN")
            if p["broken_links"]:
                flags.append(f"BROKEN:{len(p['broken_links'])}")
            flag = f"  [{' '.join(flags)}]" if flags else ""
            out.append(f"- {p['file']}  ({p['lines']} lines, {p['bytes']} bytes, last {p['last_commit'] or '?'}, inbound {p['inbound_links']}){flag}")
            for h in p["headings"][:12]:
                out.append(f"    # {h}")
            if len(p["headings"]) > 12:
                out.append(f"    ... {len(p['headings']) - 12} more headings")
            for b in p["broken_links"]:
                out.append(f"    broken -> {b}")
        if wiki.get("other_entries"):
            out.append(f"other entries: {', '.join(wiki['other_entries'])}")

    out.append("")
    out.append(f"== Project tree (depth {depth}, {info['tracked_files']} tracked files) ==")
    out.extend(render_tree(info["tree"]))
    out.append("")
    out.append("== Documentation-bearing files ==")
    if info["doc_files"]:
        out.extend(f"- {f}" for f in info["doc_files"])
    else:
        out.append("(none)")
    out.append("")
    out.append("== File types ==")
    out.extend(f"{ext:12} {n}" for ext, n in info["extensions"])
    out.append("")
    out.append("== Recent commits ==")
    out.extend(info["recent_commits"])
    return "\n".join(out)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--repo", default=".", help="path inside the project repository (default: .)")
    parser.add_argument("--wiki-dir", default=None, help="wiki clone folder (default: ../<repo-folder>.wiki)")
    parser.add_argument("--depth", type=int, default=3, help="project tree depth (default: 3)")
    parser.add_argument("--no-clone", action="store_true", help="do not clone or pull the wiki")
    parser.add_argument("--json", action="store_true", help="emit JSON instead of text")
    args = parser.parse_args()

    info = inventory_repo(args.repo, args.depth)
    wiki_dir = args.wiki_dir or os.path.join(os.path.dirname(info["root"]), info["folder"] + ".wiki")
    wiki = prepare_wiki(info, wiki_dir, args.no_clone)

    if args.json:
        print(json.dumps({"repository": info, "wiki": wiki}, indent=2))
    else:
        print(render_text(info, wiki, args.depth))

    if wiki.get("error"):
        sys.exit(1)
    problems = wiki.get("broken_link_count", 0) + wiki.get("orphan_count", 0)
    sys.exit(2 if problems else 0)

if __name__ == "__main__":
    main()
