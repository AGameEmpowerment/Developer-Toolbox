**Repository Guidance**

- **Purpose**: This folder provides curated guidance, helper files, and prompt/agent artifacts used to extend and automate developer workflows with GitHub Copilot and related agent tooling.
- **Origin**: Many example assets and conventions used here were adapted from the GitHub "awesome-copilot" collection: https://github.com/github/awesome-copilot

**Folders & Purpose**

- **`agents`**: Contains agent definition files used to declare high-level agent behavior or personas. These are typically `.agent.md` or similar descriptor files that define the role, capabilities, and conversational structure for an agent.
	- **What to expect**: One or more `.agent.md` documents describing initialization steps, activation triggers, and role-specific rules.
	- **Usage**: Use these files as templates when creating new agents for repository-specific automation, or as examples to bootstrap custom Copilot agents.

- **`collections`**: Stores curated collections of prompts, templates and small toolkits packaged together as a reusable set.
	- **What to expect**: `.collection.yml` files and metadata describing which prompts or snippets belong to a collection and how they should be used.
	- **Usage**: Import collections into local Copilot/assistant tooling or reference them when organizing reusable prompt libraries.

- **`instructions`**: Host specialized instruction files that guide Copilot behavior for different concerns (security, accessibility, platform-specific best practices, etc.).
	- **What to expect**: Markdown instruction files (for example: `security-and-owasp.instructions.md`, `a11y.instructions.md`, `performance-optimization.instructions.md`) that the repo uses to enforce consistent code generation and review policies.
	- **Usage**: Treat these documents as authoritative policy for generated code and as a source of per-repo rules that Copilot or other automated agents should follow. They are intended to be machine-consumable guidance as well as human-readable policy.

- **`prompts`**: Contains prompt templates, examples and prompt engineering recipes.
	- **What to expect**: `.prompt.md` or plain `.md` files that include example prompt text, variants, and recommended usage notes.
	- **Usage**: Copy or adapt prompt templates for new tasks; follow the documented constraints and examples to get predictable responses from the assistant.

- **`skills`**: Contains small skill modules, helper macros, or reusable capability descriptions that an agent can reference.
	- **What to expect**: JSON/YAML or markdown descriptors for discrete capabilities (for example, a `ci-deploy.skill.yaml` or `code-review.skill.md`).
	- **Usage**: Import or reference skills into agent definitions to compose higher-level behavior from tested, reusable pieces.

**Conventions & Recommendations**

- **Naming**: Use clear, dash-separated, lowercase names for files (e.g., `security-and-owasp.instructions.md`, `create-issue.prompt.md`) to keep the folder consistent and tool-friendly.
- **File Types**: Prefer Markdown for human-readable docs (`.md`) and YAML/JSON for structured metadata that automation can parse (`.yml`, `.yaml`, `.json`).
- **Attribution**: When assets are adapted from external sources (such as `github/awesome-copilot`), keep a short attribution line at the top of the file noting the upstream source and any license/usage considerations.
- **Machine Consumption**: If tools or CI jobs consume files from these folders, document the expected schema (fields and types) in an adjacent README or the same file's header.
- **Security & Privacy**: Do not store secrets, private keys, or credentials in any of these files. Use environment variables or secret stores for sensitive configuration.

**Examples**

- Agent file (example): `agents/code-review.agent.md` — describes persona, review priorities, and example prompts for the code-review agent.
- Collection file (example): `collections/secure-coding.collection.yml` — groups prompts and instructions related to secure coding and OWASP checks.
- Prompt file (example): `prompts/bug-triage.prompt.md` — a repeatable prompt template for triage incoming bug reports.

**Attribution and Source**

- These folder layouts and many example files were adapted from the GitHub "awesome-copilot" repository: https://github.com/github/awesome-copilot
- Where content was taken or adapted from that project, this repository keeps a reference to the original and retains any license/attribution notices required by the source.

**How to Contribute**

- Add new prompts, agents, collections or skills as separate files following the naming conventions above.
- When adding an instruction file that modifies Copilot behavior, double-check for conflicts with other instruction files and document the intended scope in the file header.
- Keep examples small and focused; include at least one usage example per file.

**Contact / Ownership**

- Repository owners and maintainers should be listed in `CODEOWNERS` and `AUTHORS.md`. For questions about agent behavior or to propose changes, open an issue in this repository.

