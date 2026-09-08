# Claude-Specific Skills

This root serves two purposes:

1. **Generated wrappers.** Most directories here are thin redirects generated
   by `sync_ai_assets.ps1` (repository root) from the canonical shared
   library in `.agents/skills/`. They exist so Claude Code natively discovers
   and auto-triggers shared skills. Each carries a generation marker comment;
   do not edit them — edit the canonical `SKILL.md` and re-run the script.
2. **Claude-authored skills.** Claude must create and install new
   repository-local skills here as `<name>/SKILL.md`. Files without the
   generation marker are hand-authored and are never touched by the script.

See `.ai/skills/INDEX.md` for cross-tool discovery rules and duplicate-name
precedence.
