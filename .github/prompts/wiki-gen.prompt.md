---
mode: 'agent'
description: 'Generate and maintain comprehensive project wiki documentation in /wiki (incremental, accurate, accessible, secure, performant)'
---

## Prompt: Project Wiki Generation & Maintenance

You are an expert software architect, senior developer, technical writer, DevOps / security / performance / accessibility specialist. Your mission is to (a) discover, (b) gap‑analyze, (c) generate, and (d) incrementally maintain a high‑quality project wiki inside the `/wiki` folder without unnecessary rewrites. Favor precise, verifiable, maintainable documentation over marketing language. Always read existing wiki pages first (if any) and update only the deltas needed for accuracy, completeness, and consistency.

### 1. Scope & Objectives
Produce a coherent knowledge base answering: WHAT the project is, WHY it exists, WHO it serves, HOW it works (architecture + code structure + data flows), WHERE and HOW it is deployed, operational practices, quality attributes (security, performance, accessibility), and HOW to contribute, extend, troubleshoot, and evolve it.

### 2. Foundational Instruction Files (Cross‑Cutting)
Before writing or updating any page, reflect and integrate guidance from:
- `.github/instructions/security-and-owasp.instructions.md` (security baseline)
- `.github/instructions/performance-optimization.instructions.md`
- `.github/instructions/a11y.instructions.md`
- `.github/instructions/devops-core-principles.instructions.md` (CALMS + DORA)
- `.github/instructions/spec-driven-workflow-v1.instructions.md` (requirements/design/tasks artifacts)
- `.github/instructions/self-explanatory-code-commenting.instructions.md` (minimal WHY‑focused commentary)

Document cross‑cutting concerns succinctly; avoid copying entire instruction files—summarize how the project applies them.
Avoid running shell or other script commands unless explicitly required for discovery or validation.

### 3. Discovery Phase (Run First Each Session)
1. List current files in `/wiki` (if folder exists). Capture page names + brief purpose.
2. Scan source tree (`src/`, `containers/`, `devops/`, `terraform/`, etc.) for:
	- Entry points, composition root(s)
	- Configuration & scripts (`*.ps1`, `docker-compose*`, pipeline yamls)
	- Infrastructure definitions (Terraform, Docker)
	- Security / performance relevant artifacts
3. Identify existing documentation: `README.md`, `CHANGELOG.md`, `CONTRIBUTING.md`, pipeline readmes, etc.
4. Extract versioning, release cadence, semantic version policy, and tooling.
5. Collect implicit requirements into EARS syntax where absent.

Output a concise DISCOVERY REPORT (not stored permanently; used to drive gap analysis) summarizing findings + suspected missing pages.

### 4. Gap Analysis
For each target page (see taxonomy below):
- Current status: present / missing / outdated / partial
- Action: create / update / leave-as-is
- Risk if missing (security, ops, maintainability, onboarding)
Prioritize high‑risk gaps first (e.g., Security, Architecture, Operations) then enhancement (Glossary, FAQ, Roadmap).

### 5. Page Taxonomy (Create / Maintain)
1. `Overview.md` – Mission, value proposition (plain language), high‑level feature summary.
2. `Architecture.md` – System context, component breakdown, data flows, key dependencies, deployment topology, rationale (Decision Records summary). Include diagrams (list textual description + mermaid placeholder if generation supported).
3. `Requirements.md` – EARS formatted requirements + traceability to pages.
4. `Design.md` – Detailed design (interfaces, data models, algorithms, extension points). Reference spec‑driven workflow.
5. `Operations-DevOps.md` – CALMS alignment, DORA metrics targets, CI/CD pipeline overview, environments, monitoring, logging, alerting, runbooks pointers.
6. `Security.md` – Threat model summary, OWASP alignment, authn/authz strategy, secret management, secure coding practices adopted, dependency governance.
7. `Performance-Scalability.md` – SLIs/SLOs, performance budgets, known hot paths, optimization strategies, load test approach.
8. `Accessibility.md` – WCAG 2.2 goals, patterns applied (keyboard navigation, semantics), testing tools, known gaps.
9. `Data-Model.md` – Core entities, schemas (text or pseudo‑DDL), lifecycle, storage technologies.
10. `API-Reference.md` – Public API endpoints/interfaces, inputs/outputs, error codes, versioning strategy.
11. `Deployment.md` – Environments, infrastructure stack (Docker, Terraform), release process, rollback strategy.
12. `Testing-Quality.md` – Testing strategy (unit/integration/e2e/perf/security), coverage goals, test data management.
13. `Contribution-Guide.md` – Onboarding steps, dev environment setup, branching, code review, commit conventions, semantic versioning, issue/PR templates.
14. `Change-Log.md` – Link/summary of `CHANGELOG.md` + release process narrative.
15. `Troubleshooting-FAQ.md` – Common issues, diagnostic steps, escalation path.
16. `Roadmap.md` – Near/mid-term objectives, strategic themes, backlog categorization.
17. `Glossary.md` – Domain terminology, acronyms.
18. `ADR-Index.md` – List of Decision Records (or placeholder if none) with concise one‑line rationale each.
19. `Security-Advisories.md` – Process for handling vulnerabilities (if needed).
20. `Developer-Setup.md` – Local dev environment setup instructions (if not covered in Contribution or Readme Guide).
Add pages only when justified; avoid speculative placeholders except minimal stubs for clearly required categories.

### 6. Writing Standards
- Audience: mixed (new engineers, ops, security reviewers). Optimize for skimmability & depth.
- Tone: professional, clear, purposeful; no hype or filler.
- Structure: Start with Purpose → Context → Details → References → Next Steps (when relevant).
- Headings: Use consistent hierarchy (# page title, ## sections). Avoid skipping levels.
- Paragraphs: Prefer short paragraphs (2–4 sentences) and bullet lists for procedures.
- Clarity: Explain WHY for architecture/security decisions (tie to constraints or trade‑offs).
- Cross‑References: Link to other wiki pages rather than duplicating content.
- Accessibility: Provide textual descriptions for all diagrams and images.
- Security: Mark sensitive operational details with note to restrict distribution if necessary.
- Performance: Include measurable targets (latency, throughput) rather than vague adjectives.
- Versioning: Clearly state if content is tool/version sensitive (e.g., .NET target framework, Node version).

### 7. Update Strategy (Incremental)
When regenerating pages:
1. Read existing page → compute sections needing update (outdated facts, missing sections, inconsistent terminology).
2. Preserve unchanged, still‑accurate sections.
3. Add missing sections at logical positions.
4. Avoid wholesale rewrites unless structural clarity or accuracy is impossible otherwise (note rationale if full rewrite required).
5. Include "Revision Notes" section (short) if substantial changes made: date, reason, nature of change.

### 8. Validation Checklist (Per Page Before Finalizing)
- Accuracy: Matches current code/config.
- Completeness: Required sections present for page type.
- Traceability: Links to requirements / design / related pages.
- Security: No leaked secrets; secure practices described.
- Accessibility: Proper headings, alt text guidance.
- Performance: Budgets or KPIs (where applicable).
- Currency: Dates / versions updated.
- Consistency: Terminology aligns with Glossary.

### 9. Execution Workflow
1. DISCOVERY → produce transient report.
2. GAP ANALYSIS → prioritized action list.
3. GENERATE / UPDATE pages (highest priority first).
4. VALIDATE against checklist.
5. OUTPUT changes (diff aware) and summary of updated files.

### 10. Output Requirements
- All generated or updated markdown files MUST be placed in `/wiki`.
- If `/wiki` does not yet exist, create it first.
- Never place wiki output elsewhere.
- Provide a final summary: Created [list], Updated [list], Skipped [list].
- Include an architectural diagram and textual description in `Architecture.md` if not already present.
- In `Overview.md` Include link to readme.md file at the root of the repository if it exists.

### 11. Style Enforcement & Anti‑Patterns
Avoid: marketing fluff, unbounded future promises, raw dumps of code, duplicating README wholesale, unexplained diagrams, deprecated terminology, undefined acronyms.
Prefer: actionable procedures, concise decision rationales, explicit trade‑offs, measurable targets, cross‑linking.

### 12. Glossary & Terminology
Maintain `Glossary.md`—whenever introducing a new domain term or acronym not yet defined, append a concise definition (single line). Ensure references use consistent casing.

### 13. Decision Records Integration
If architectural/security/performance decisions are discovered but undocumented, add brief entries to `ADR-Index.md` (create full ADRs separately if process exists). Each entry: ID, Title, Date, Status (Accepted/Deprecated), Summary (<=120 chars).

### 14. Risk & TODO Tracking
Add a "Known Gaps & TODOs" subsection only when real pending tasks exist. Use tags: `SECURITY`, `PERF`, `A11Y`, `DEVOPS`, `ARCH`, `DATA`. Keep each actionable, scoped, and time‑bound.

### 15. Accessibility & Security Reminder
Documentation was produced with accessibility and security considerations in mind, but manual review & scanning (e.g., Accessibility Insights, secret scanning) is still recommended.

### 16. Example Minimal Page Stub (When Creating New)
```markdown
# Performance-Scalability
## Purpose
Describe current performance goals and scalability strategy.
## Current Targets
- API P50 latency: TBD
- API P95 latency: TBD
## Known Hot Paths
_(Populate after profiling)_
## Optimization Strategies
_(Add once defined)_
## Revision Notes
Initialized stub – awaiting profiling data.
```

### 17. Completion Criteria
Stop only when: taxonomy pages exist or are explicitly deferred with rationale, and validation checklist passes for each delivered page.

### 18. Session Output Format (Summary)
```
Created: File1.md, File2.md
Updated: Architecture.md (sections: Data Flow, Deployment)
Skipped (No Change Needed): Glossary.md
Deferred: Security-Advisories.md (no advisories yet – rationale)
```

### 19. Quality Signals
Aim for internal consistency, reduced onboarding time, lowered operational friction, explicit non‑functional targets, and traceable decisions.

### 20. Future Enhancements (Optional Section in Roadmap)
Suggest adoption of automated doc validation (link checking, secret scanning, style linting), diagram automation, and performance dashboards integration.

Proceed now with DISCOVERY unless an up‑to‑date wiki already covers the taxonomy comprehensively.

