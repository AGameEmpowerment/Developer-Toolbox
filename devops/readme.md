# DevOps

DevOps-related examples live here. These files are templates for the local
Developer Toolbox and are not production-ready pipeline, infrastructure, or
deployment configuration.

Copy these examples into a real project only after replacing all sample names,
service connections, variable groups, repositories, package metadata, and
environment assumptions.

- [Pipelines](pipelines/readme.md)
- [Terraform](terraform/readme.md)
- [Manifests](manifest/readme.md)

`PrepNuget.ps1` and `DeveloperToolbox.Example.Lib.nuspec` provide a generic,
public-feed-compatible packaging example for the sample library. Prefer the
SDK-style `dotnet pack` pipeline unless a NuSpec file is specifically required.
