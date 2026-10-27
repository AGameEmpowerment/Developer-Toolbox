
## Unreleased

- Allow 300 seconds for cold OIDC startup qualification and verify the current
  image with all 25 live checks; update MailKit and the SQL cryptography dependency
  to patched packages, and record Java 25/Maven build and test evidence.
- Align common, Azure and AWS resource groups, initialization, environment handling,
  image preparation and local OIDC builds with the reference baseline.
- Expand the independent .NET, Java, Node.js and Python examples and tests; use
  seven NUnit projects with NSubstitute for .NET.
- Keep public package sources, neutral names, existing AI integrations and the
  public frontend; update image policy, validation workflows and wiki evidence.

# Changelog

0.0.1 - Initial commit
0.0.2 - Updated README with additional instructions
0.0.3 - Refreshed repository and wiki documentation, removed obsolete `.github/scripts` references, and documented current review findings
0.0.4 - Added cross-platform Docker image pull parity and improved Docker setup/teardown resilience
0.1.0 - Ported public Toolbox updates: documented the ai-asset-curator skill, hardened Docker/Podman selection, maintained .NET 10 Dev Container support, refreshed application/config scaffolding across Next.js, .NET, and manifests, consumer bootstrap installers and tests, GitHub wiki and PR-review skills, refreshed AI guidance, and generated Claude/Codex adapters using public resources throughout
0.1.1 - Replaced the unrelated application starter with a generic .NET 10 and Next.js example based on the source Toolbox structure, using public-only dependencies and deterministic local data
0.1.2 - Clarified the setup wrapper's installed location and aligned the example disclaimer with the cool-background color guide
