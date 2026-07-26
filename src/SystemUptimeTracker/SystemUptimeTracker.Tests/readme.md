# Unit Tests

Unit tests go in folders that match the folder structure of the web project. A general rule of thumb is to make sure you have unit tests for all your types that implement an interface.
We are currently missing tests on things in the Connection folder as they were acting up, and we may move some of those to a shared library.

## Repository Tests

The repository tests show why we use the HttpClientWrapper. This lets us verify that all our repositories make the appropriate HTTP requests to the expected urls.

## Integration Suite Commands

Run the focused integration suite locally with:

```powershell
dotnet test src/SystemUptimeTracker/SystemUptimeTracker.Tests/SystemUptimeTracker.Tests.csproj --configuration Development --filter "TestCategory=Integration"
```

The CI lane runs the same test project without a category filter. The contract is defined in `devops/pipelines/Build-SystemUptimeTracker.yml` and currently uses:

```powershell
dotnet test src/SystemUptimeTracker/SystemUptimeTracker.Tests/SystemUptimeTracker.Tests.csproj --configuration $(buildConfiguration) --no-build --logger "trx;LogFileName=SystemUptimeTracker.Tests.trx" --results-directory "$(Agent.TempDirectory)/TestResults/dotnet" --collect "XPlat Code Coverage"
```

## General Tips and Tricks

### Thoughtful use of Args.Any.

Where possible, don't use Arg.Any in your assertions. It is better to do things like what you see in the CountriesFactory tests where we actually assert that we receive the object we expect in a parameter.
Those kinds of changes are easy to do if you think about it up front, and they make your tests a lot stronger. As a general rule, shy away from Arg.Any. Use Arg.Is<> in places where you can't get the exact value for comparison.

### Tests for Sorting, and Child Converts

Look at the CountriesFactory tests for an example of how to test sorting. We also have tests for the child converts in the CountriesFactory tests.
Notice that we verify that it converted each object, it sorted them in order, **and** that we only got 2 converts total. That's a simple way to strengthen the test
and make sure you get exactly the converts you expect.

### Try to avoid private helpers

Technically this is something in your implementation rather than your tests, but we do it so we can test more effectively. Anything you are thinking of doing private,
do it as a public virtual method instead. Leave it off the interface definition so that consuming code doesn't see it, but you can override it in your tests.
Then in your tests, you do Substitute.ForPartsOf<ClassDefinition>(constructorParamsHere)... That lets you then override the method in a test.

service.When(s => s.SomeVirtualMethod(5)).DoNotCallBase();
service.When(s => s.SomeVirtualMethod(5)).Returns(10);
//now execute the test you want

service.Received(1).SomeVirtualMethod(5);

That approach lets you verify you run your helper method.  You can then unit test that SomeVirtualMethod in isolation and verify that you call the helper
like you would any other dependencies.
