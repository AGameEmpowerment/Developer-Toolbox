---
name: csharp-nunit
description: 'Write and migrate NUnit tests for C# projects, including parameterized cases, asynchronous assertions, fixture isolation, and NSubstitute mocks.'
---

# NUnit Best Practices

Use NUnit for .NET tests in this repository. Use NSubstitute when interface
mocks are needed; retain stateful simulators that exercise resource lifetimes,
ordering, cancellation or message delivery.

## Project Setup

- Keep tests in a separate `[ProjectName].Tests` project.
- Reference `Microsoft.NET.Test.Sdk`, `NUnit` and `NUnit3TestAdapter` using the
  repository's selected versions. Follow the NuGet Manager skill for package edits.
- Add `NSubstitute` only to projects that use it. Substitute interfaces or virtual
  members; do not substitute non-virtual methods or change production APIs solely for mocking.
- Run tests using `dotnet test`; preserve the existing target framework.

## Cases and Isolation

- Use `[Test]` for a parameterless case, `[TestCase(...)]` for inline rows and
  `[TestCaseSource(nameof(Cases))]` for a static source. Do not add `[Test]` to
  a method that already has parameterized case attributes.
- Name tests `MethodName_Scenario_ExpectedBehavior` and keep each method focused.
- Use `[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]` when tests require fresh
  instances. Use `[SetUp]` and `[TearDown]` for per-case setup and cleanup.
- Shared one-time setup must respect the selected fixture lifecycle. Do not enable
  parallelism for tests that mutate environment variables or other global state.
- Use `[Category("Integration")]` and explicit opt-in configuration for live
  resources. Report a clear `Assert.Ignore` reason when the opt-in is absent.
- Clean up only resources owned by the test; persistent shared Toolbox services
  and consumer resources must remain intact.

## Assertions and Mocks

- Prefer `Assert.That(actual, Is.EqualTo(expected))`, `Is.SameAs`, `Is.True`,
  `Is.Null`, `Does.Contain` and `Has.Count.EqualTo` constraints.
- Preserve exact-type checks (`Is.TypeOf<T>`, `Assert.Throws<T>`) versus
  subtype checks (`Is.InstanceOf<T>`, `Assert.Catch<T>`).
- NUnit 5 exception assertions are asynchronous: await `Assert.ThrowsAsync<T>`
  and `Assert.CatchAsync<T>`. Keep asynchronous tests returning `Task`.
- Use `Substitute.For<IContract>()`, explicit `.Returns(...)` behavior, and
  `.Received(...)`/`.DidNotReceive()` checks for meaningful interactions.
- Keep assertion strength and all parameter rows when migrating tests. Do not
  introduce an assertion compatibility shim.

## Evidence

- Record authored methods separately from adapter-expanded/discovered cases and
  executed passed, failed and skipped results.
- Preserve dated source inventories and execution evidence. Coverage requires a
  measured report; test counts alone do not establish coverage or assertion quality.
