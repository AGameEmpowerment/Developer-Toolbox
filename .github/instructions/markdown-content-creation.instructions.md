---
description: 'Markdown standards for installation documentation'
applyTo: 'INSTALL-*.md'
---

## Installation Documentation Rules

Use these rules for repository installation guides and setup walkthroughs.

1. **Purpose**: Start with a short summary of what the installation guide sets up.
2. **Prerequisites**: List required tools, versions, credentials, and permissions before any commands.
3. **Steps**: Use numbered steps for ordered setup work. Keep each step action-oriented.
4. **Commands**: Use fenced code blocks with the appropriate language tag, such as `powershell`, `bash`, `yaml`, or `json`.
5. **Configuration**: Call out required environment variables, secrets, file paths, and defaults explicitly.
6. **Validation**: Include a verification section with commands or checks that prove the setup worked.
7. **Troubleshooting**: Include known failure modes and practical recovery steps when setup has external dependencies.
8. **Links**: Use descriptive link text and prefer repository-relative links for local files.
9. **Line Length**: Keep lines readable. Break long paragraphs and command explanations before they become difficult to scan.

## Formatting And Structure

- Use `##` for major sections and `###` for subsections.
- Use `-` for unordered lists and `1.` for ordered setup steps.
- Keep one command or closely related command group per fenced code block.
- Prefer tables for environment variables, settings, ports, or version matrices.
- Avoid blog-post metadata, marketing copy, categories, featured images, and author front matter.

## Validation Checklist

- [ ] The guide names the target operating system or shell when commands are shell-specific.
- [ ] Required tools and versions are discoverable before setup begins.
- [ ] Secrets and credentials are referenced by location or variable name, not by value.
- [ ] Setup commands can be copied without hidden prompts or missing context.
- [ ] Validation steps describe the expected successful result.
- [ ] Troubleshooting notes cover the most likely setup failures.
