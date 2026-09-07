# DevOps Pipelines

Pipeline YAML examples are stored in this folder.

These files are templates for local Developer Toolbox reference only. They use
disabled triggers and example values so they are not accidentally treated as
production pipelines.

Before copying one into a real project, replace every example path, service
connection, variable group, registry, package name, and environment value.

## Public example pipelines

- `Project-Build.yml` builds and tests the .NET and Next.js examples.
- `Project-Build-Docker.yml` builds the client from a public Node.js image and
  does not push it to a registry.
- `Project-Build-CloudFoundry.yml` produces a generic buildpack-ready client
  artifact without connecting to a platform.
- `Project-NuGet-Build.yml` creates an SDK-style NuGet package artifact.
- `Project-NuGet-NuSpec-Build.yml` demonstrates equivalent NuSpec packaging.

The package pipelines deliberately stop at publishing pipeline artifacts. A
consuming project must add its own reviewed feed authentication and publishing
stage.
