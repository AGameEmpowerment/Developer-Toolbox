using SystemUptimeTracker.Common.Helpers.Filter;

namespace SystemUptimeTracker.Tests.Common;

[TestFixture(Category = "Unit")]
public class StarRedactorTests
{
    private StarRedactor _starRedactor;

    [SetUp]
    public void SetUp()
    {
        _starRedactor = new StarRedactor();
    }

    [Test]
    public void Redact_ShouldFillDestinationWithAsterisks()
    {
        // Arrange
        ReadOnlySpan<char> source = "SensitiveData".AsSpan();
        char[] destination = new char[source.Length];

        // Act
        int result = _starRedactor.Redact(source, destination);

        // Assert
        Assert.That(result, Is.EqualTo(destination.Length));
        Assert.That(destination, Is.All.EqualTo('*'));
    }

    [Test]
    public void GetRedactedLength_ShouldReturnLengthOfInput()
    {
        // Arrange
        ReadOnlySpan<char> input = "SensitiveData".AsSpan();

        // Act
        int result = _starRedactor.GetRedactedLength(input);

        // Assert
        Assert.That(result, Is.EqualTo(input.Length));
    }
}