# Developer Toolbox example

This local-only sample mirrors the source toolbox layout with a .NET console
application, reusable class library, unit tests, and a Next.js client. It uses
only public package registries and generic example data.

Build and test the .NET projects from the repository root:

```powershell
dotnet build .\DeveloperToolbox.Example.slnx
dotnet test .\src\DeveloperToolbox.Example\DeveloperToolbox.Example.Tests\DeveloperToolbox.Example.Tests.csproj --no-build
```

See [DeveloperToolbox.Example.Client](DeveloperToolbox.Example.Client/README.md)
for the client commands.

Generic, disabled-by-default Azure Pipelines examples are available under
[`devops/pipelines`](../../devops/pipelines/readme.md). They build, test,
containerize, and package this sample without private repositories or feeds.
