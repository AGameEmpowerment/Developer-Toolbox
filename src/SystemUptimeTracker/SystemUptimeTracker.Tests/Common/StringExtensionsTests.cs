using SystemUptimeTracker.Common.Helpers.Extensions;

namespace SystemUptimeTracker.Tests.Common;

[TestFixture(Category = "Unit")]
public class StringExtensionsTests
{
    [Test]
    public void ScrubUrlRoute_WithDoubleSlashes_ReplacesWithSingleSlash()
    {
        // Arrange
        string url = "http://example.com//path//to//resource";

        // Act
        string? result = url.ScrubUrlRoute();

        // Assert
        Assert.That(result, Is.EqualTo("http://example.com/path/to/resource"));
    }

    [Test]
    public void ScrubUrlRoute_WithNoDoubleSlashes_ReturnsSameUrl()
    {
        // Arrange
        string url = "http://example.com/path/to/resource";

        // Act
        string? result = url.ScrubUrlRoute();

        // Assert
        Assert.That(result, Is.EqualTo(url));
    }

    [Test]
    public void ScrubUrlRoute_WithEmptyString_ReturnsEmptyString()
    {
        // Arrange
        string url = string.Empty;

        // Act
        string? result = url.ScrubUrlRoute();

        // Assert
        Assert.That(result, Is.EqualTo(url));
    }

    [Test]
    public void ScrubUrlRoute_WithNullString_ReturnsNull()
    {
        // Arrange
        string? url = null;

        // Act
        string? result = url.ScrubUrlRoute();

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void ScrubUrlRoute_WithHttpProtocol_PreservesProtocol()
    {
        // Arrange
        string url = "http://example.com//path";

        // Act
        string? result = url.ScrubUrlRoute();

        // Assert
        Assert.That(result, Is.EqualTo("http://example.com/path"));
        Assert.That(result, Does.StartWith("http://"));
    }

    [Test]
    public void ScrubUrlRoute_WithHttpsProtocol_PreservesProtocol()
    {
        // Arrange
        string url = "https://example.com//path";

        // Act
        string? result = url.ScrubUrlRoute();

        // Assert
        Assert.That(result, Is.EqualTo("https://example.com/path"));
        Assert.That(result, Does.StartWith("https://"));
    }

    [Test]
    public void ScrubUrlRoute_WithoutProtocol_ReplacesDoubleSlashes()
    {
        // Arrange
        string url = "/api//endpoint//path";

        // Act
        string? result = url.ScrubUrlRoute();

        // Assert
        Assert.That(result, Is.EqualTo("/api/endpoint/path"));
    }

    [Test]
    public void Mask_WithStringLongerThanFourCharacters_MasksCorrectly()
    {
        // Arrange
        string input = "1234567890";

        // Act
        string? result = input.Mask();

        // Assert
        Assert.That(result, Is.EqualTo("1234********"));
    }

    [Test]
    public void Mask_WithStringOfFourCharacters_ReturnsMaskedString()
    {
        // Arrange
        string input = "1234";

        // Act
        string? result = input.Mask();

        // Assert
        Assert.That(result, Is.EqualTo("****"));
    }

    [Test]
    public void Mask_WithStringOfThreeCharacters_ReturnsMaskedString()
    {
        // Arrange
        string input = "123";

        // Act
        string? result = input.Mask();

        // Assert
        Assert.That(result, Is.EqualTo("****"));
    }

    [Test]
    public void Mask_WithStringOfOneCharacter_ReturnsMaskedString()
    {
        // Arrange
        string input = "1";

        // Act
        string? result = input.Mask();

        // Assert
        Assert.That(result, Is.EqualTo("****"));
    }

    [Test]
    public void Mask_WithWhitespaceOnlyString_ReturnsWhitespaceString()
    {
        // Arrange
        string input = "   ";

        // Act
        string? result = input.Mask();

        // Assert
        Assert.That(result, Is.EqualTo(input));
    }

    [Test]
    public void Mask_WithEmptyString_ReturnsEmptyString()
    {
        // Arrange
        string input = string.Empty;

        // Act
        string? result = input.Mask();

        // Assert
        Assert.That(result, Is.EqualTo(input));
    }

    [Test]
    public void Mask_WithNullString_ReturnsNull()
    {
        string? input = null;
        string? result = input.Mask();
        Assert.That(result, Is.Null);
    }
}
