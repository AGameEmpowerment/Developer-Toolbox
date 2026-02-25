# GitHub Copilot Setup Documentation

This repository is configured with comprehensive GitHub Copilot instructions following the best practices outlined at https://gh.io/copilot-coding-agent-tips.

## 📋 Configuration Overview

### ✅ Required Components

All required components are properly configured:

- **Main Instructions**: `.github/copilot-instructions.md` - Primary instructions for Copilot
- **Instructions Directory**: `.github/instructions/` - 42 specialized instruction files
- **Validation Workflow**: `.github/workflows/copilot-setup-steps.yml` - Automated validation

### 🎯 Optional Components

The repository includes these optional but recommended components:

- **Custom Agents**: `.github/agents/` - 13 specialized agent definitions
- **Prompts**: `.github/prompts/` - 40 reusable prompt templates
- **Collections**: `.github/collections/` - 28 curated collections

## 🛠️ How It Works

### Main Instructions File

The `.github/copilot-instructions.md` file contains:
- Repository context and structure
- Code quality standards
- General development guidelines
- References to specialized instructions

### Specialized Instructions

Each file in `.github/instructions/` provides context-specific guidance:

```yaml
---
applyTo: '**/*.cs'
description: 'C# development guidelines'
---
```

The `applyTo` field uses glob patterns to target specific file types, ensuring Copilot uses the right instructions for each context.

### Skills Discovery

Use `.github/skills/INDEX.md` as the canonical map for:

- instruction-driven skills under `.github/skills/*/SKILL.md`
- Skills CLI discovery and installation workflows (`npx skills find`, `npx skills add`)

### Custom Agents

Agent files define specialized behaviors for different tasks:
- Code review specialists
- Testing experts
- Security auditors
- Implementation planners

### Prompts

Reusable prompt templates help maintain consistency across:
- Bug triage
- Feature implementation
- Code reviews
- Documentation generation

### Collections

Collections group related prompts, agents, and instructions for specific workflows:
- Security best practices
- Azure cloud development
- Testing automation
- DevOps operations

## 🔍 Validation

### Manual Validation

Run the validation script locally:

```bash
node .github/scripts/validate-copilot-setup.js
```

### Automated Validation

The workflow `.github/workflows/copilot-setup-steps.yml` automatically validates:
- Main instructions file exists and is not empty
- Instructions directory contains files with proper frontmatter
- Optional components (agents, prompts, collections) are properly formatted

The workflow runs on:
- Push to `.github/**` paths
- Pull requests affecting `.github/**`
- Manual workflow dispatch

### Expected Output

A successful validation shows:

```
🔍 Validating GitHub Copilot setup...

📊 Validation Results:

ℹ️  Information:
  ✓ Main instructions file exists (12221 bytes)
  ✓ Found 42 instruction files
    - 41 files with applyTo patterns
    - 42 files with descriptions
  ✓ Found 13 agent files
    - 13 agents with proper frontmatter
  ✓ Found 40 prompt files
  ✓ Found 28 collection files

✅ Copilot setup validation passed!
```

## 📚 Instruction Files by Category

### Platform & Languages

- `csharp.instructions.md` - C# development
- `dotnet-framework.instructions.md` - .NET Framework specifics
- `nodejs-javascript-vitest.instructions.md` - Node.js with Vitest
- `typescript.instructions.md` - TypeScript development
- `python.instructions.md` - Python development

### Web Development

- `reactjs.instructions.md` - React applications
- `nextjs.instructions.md` - Next.js framework
- `blazor.instructions.md` - Blazor applications
- `aspnet-rest-apis.instructions.md` - ASP.NET REST APIs
- `html-css-style-color-guide.instructions.md` - Frontend styling

### Cloud & Infrastructure

- `azure-*.instructions.md` - Azure services (Functions, Logic Apps, DevOps, etc.)
- `terraform.instructions.md` - Infrastructure as Code
- `containerization-docker-best-practices.instructions.md` - Docker & containers

### Quality & Security

- `security-and-owasp.instructions.md` - Security best practices
- `a11y.instructions.md` - Accessibility guidelines
- `performance-optimization.instructions.md` - Performance optimization
- `code-review-generic.instructions.md` - Code review standards

### DevOps & Tooling

- `devops-core-principles.instructions.md` - DevOps fundamentals
- `azure-devops-pipelines.instructions.md` - Azure DevOps YAML
- `makefile.instructions.md` - Makefile development
- `powershell.instructions.md` - PowerShell scripting

### Documentation & Meta

- `markdown.instructions.md` - Markdown formatting
- `prompt.instructions.md` - Prompt file creation
- `agents.instructions.md` - Agent file creation
- `collections.instructions.md` - Collection management

## 🎓 Using Copilot with This Setup

### In Your IDE

When you work on files in this repository, Copilot automatically:

1. Loads the main instructions from `.github/copilot-instructions.md`
2. Applies file-specific instructions based on `applyTo` patterns
3. Makes custom agents available in the chat interface
4. Provides context-aware suggestions

### Chat Commands

Use these patterns for best results:

```
# Use a specific agent
@code-review Please review this PR

# Reference a prompt
Use the bug-triage prompt for this issue

# Request specific guidance
Follow the security instructions when implementing authentication
```

### Best Practices

1. **Read the instructions**: Review `.github/copilot-instructions.md` for repository context
2. **Use appropriate agents**: Select agents that match your task
3. **Follow patterns**: Use existing prompts and collections as templates
4. **Validate changes**: Run the validation script when modifying configuration

## 🔧 Maintenance

### Adding New Instructions

1. Create a new file in `.github/instructions/`
2. Add YAML frontmatter with `applyTo` and `description`
3. Write clear, actionable instructions
4. Run validation: `node .github/scripts/validate-copilot-setup.js`

### Creating New Agents

1. Create a new file in `.github/agents/` with `.agent.md` extension
2. Add frontmatter with required fields
3. Define agent behavior and capabilities
4. Test with Copilot Chat

### Updating Collections

1. Edit files in `.github/collections/`
2. Ensure references to prompts and agents are valid
3. Update collection metadata as needed

## 📖 References

- **Best Practices Guide**: https://gh.io/copilot-coding-agent-tips
- **Awesome Copilot Collection**: https://github.com/github/awesome-copilot
- **Repository README**: `.github/readme.md`

## ✅ Status

This repository is **fully configured** with GitHub Copilot instructions and validated by automated checks.

Last validated: See workflow runs in Actions tab

---

For questions or issues with the Copilot setup, please open an issue in this repository.
