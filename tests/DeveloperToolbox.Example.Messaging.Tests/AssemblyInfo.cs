using NUnit.Framework;

// Isolate fixture state for each expanded test case.
[assembly: FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
