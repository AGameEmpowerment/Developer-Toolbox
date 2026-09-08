---
name: toolbox-repository-bridge
description: 'Bridge a consuming project repository with the public AGameEmpowerment/Developer-Toolbox after Install-ToolboxSetup.ps1 or install_toolbox_setup.sh has been run. Use this when working with copied Toolbox bootstrap files, local emulators, the project devcontainer, DEVELOPER_TOOLBOX_ROOT, or deciding which repository owns a change.'
---

# Toolbox Repository Bridge

Use the installed bootstrap as a deliberate boundary between the current project
repository and the public `AGameEmpowerment/Developer-Toolbox` repository. Keep
project code and project-specific configuration local while reusing Toolbox
services, setup automation, and devcontainer conventions.

## Establish The Two Repositories

1. Resolve the current Git worktree root. Treat it as the consumer repository.
2. Confirm the bootstrap exists by checking for the root `setup_toolbox.*`
   wrappers, the launchers under `setup/`, and the inventory in
   `setup/toolbox-bootstrap-files.txt`.
3. Resolve the Toolbox checkout in this order:
   - A path explicitly supplied by the user.
   - `DEVELOPER_TOOLBOX_ROOT` when it is set.
   - `Developer-Toolbox` beside the consumer repository.
4. When no checkout exists, stop Toolbox-dependent work and ask the user to
   clone Developer Toolbox or provide its existing clone path. Recommend one
   of the copied launchers with `-SkipSetup` or `--skip-setup`; do not clone the
   Toolbox silently on the user's behalf or reproduce the launcher's
   authentication logic in ad hoc commands.
5. Before editing either repository, read its own `AGENTS.md` and authoritative
   policy. The repositories may have different worktree state and instructions.

## Keep Ownership Clear

Make changes in the consumer repository when they are specific to its product,
source code, tests, dependencies, pipelines, or configuration. The copied
devcontainer name is intentionally the consumer repository directory name.

Make changes in the Toolbox checkout when they improve reusable bootstrap
templates, shared emulator definitions, launcher behavior, or setup automation
for every consuming project. Update canonical AI sources and regenerate their
adapters there; never edit generated adapters directly.

Every target path listed in `setup/toolbox-bootstrap-files.txt` is Toolbox-owned,
including this skill's canonical `.agents/skills/toolbox-repository-bridge/SKILL.md` and its
`.claude/skills` wrapper. The wrapper's generation marker names
`sync_ai_assets.ps1`, which exists only in the Toolbox checkout; the consumer has no
generator. Edit the skill in the Toolbox, regenerate the wrapper there, and then
refresh the consumer copies as described below.

The Toolbox does not carry runnable `setup_toolbox.*` files at its repository
root. It stores those two convenience wrappers as disabled
`setup/setup_toolbox.ps1_` and `setup/setup_toolbox.sh_` templates. Both
installers resolve the root target entries in the manifest from those templates
and remove the trailing underscore while copying them to a consumer.

When a reusable Toolbox improvement also needs to reach the current consumer:

1. Implement and verify it in the Toolbox checkout.
2. Re-run the appropriate installer from the Toolbox checkout. The installer only
   adds paths that are missing from the consumer; it never overwrites an existing
   file or adds children to an existing directory entry, so this step delivers new
   bootstrap paths only.
3. For bootstrap files the consumer already has, compare each consumer copy with
   its Toolbox source using `setup/toolbox-bootstrap-files.txt` as the list of
   paths. Review consumer-local edits first, then either replace the file from the
   Toolbox checkout or re-apply the local edits on top of the new version. Keep
   the consumer's display name when refreshing `.devcontainer/devcontainer.json`.
4. Report which files were added, which were replaced, and which were left
   unchanged, and say why for each replaced or unchanged file.

Do not copy application code, secrets, generated credentials, container data, or
unlisted Toolbox files into the consumer repository.

## Use The Installed Entry Points

From the consumer repository, prefer these commands:

```powershell
./setup_toolbox.ps1
./setup_toolbox.ps1 -SkipSetup
./setup_toolbox.ps1 -NoUpdate
./setup_toolbox.ps1 -ToolboxPath C:\path\to\Developer-Toolbox
```

```bash
./setup_toolbox.sh
./setup_toolbox.sh --skip-setup
./setup_toolbox.sh --no-update
./setup_toolbox.sh --path /path/to/Developer-Toolbox
```

The root scripts are thin convenience wrappers. The implementation launchers
remain under `setup/` and may be called directly when diagnosing the bootstrap.

Use `.devcontainer/start_devcontainer.ps1` or its Bash companion to start the
consumer devcontainer. Inside it, invoke the matching
`.devcontainer/start_developer_toolkit.ps1` or `.sh` launcher. It refuses to run
outside a devcontainer or Codespace and delegates through the project launcher
when the repository does not contain Toolbox's own `docker_setup.*` script.

Use only public package registries and public container images. Never request,
print, copy, or commit credential values.

## Diagnose The Bridge

Check failures in this order:

1. The current directory is the exact consumer Git root.
2. The expected files in `setup/toolbox-bootstrap-files.txt` exist, including
   the root wrappers and their `setup/_toolbox.*` implementation launchers.
3. Git and either PowerShell or Bash are available for the selected launcher.
4. The resolved Toolbox path is a Git repository root, not a nested directory.
5. The checkout has the requested branch and access to its `origin` remote.
6. Docker or Podman is installed and its engine is reachable.
7. The devcontainer uses the consumer repository name and its post-create script
   is present.

If the Toolbox clone is missing, report that as the first actionable finding and
ask the user to run one of these clone-or-update commands before continuing. They
clone a missing checkout, or fetch and fast-forward an existing one; add
`-NoUpdate` or `--no-update` to leave an existing checkout untouched.

```powershell
./setup_toolbox.ps1 -SkipSetup
```

```bash
./setup_toolbox.sh --skip-setup
```

Report which repository each inspected or changed file belongs to. When a fix
spans both repositories, verify each worktree separately and summarize the two
sets of changes separately.
