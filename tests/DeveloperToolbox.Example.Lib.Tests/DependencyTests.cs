using DeveloperToolbox.Example.Lib;
using NUnit.Framework;

namespace DeveloperToolbox.Example.Lib.Tests;

public sealed class DependencyTests
{
    [Test]
    public void GetMessage_NewDependency_ReturnsDefaultSampleGreeting()
    {
        Assert.That(new Dependency().GetMessage(), Is.EqualTo("Hello from Dependency!"));
    }
}
