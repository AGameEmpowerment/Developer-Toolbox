using DeveloperToolbox.Example.Lib;
using Xunit;

namespace DeveloperToolbox.Example.Tests;

public sealed class DependencyTests
{
    [Fact]
    public void GetMessage_DefaultDependency_ReturnsGenericExampleMessage()
    {
        var dependency = new Dependency();

        var message = dependency.GetMessage();

        Assert.Equal("Hello from the Developer Toolbox example!", message);
    }
}
