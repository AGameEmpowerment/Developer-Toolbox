using SystemUptimeTracker.Common.Helpers;

namespace SystemUptimeTracker.Tests.Common;

[TestFixture(Category = "Unit")]
public class GuidFunctionsTests
{
    [Test]
    public void IsGreaterThan_LeftGuidGreaterThanRightGuid_ReturnsTrue()
    {
        // Arrange - Use specific GUIDs where left > right
        Guid left = new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff");
        Guid right = new Guid("00000000-0000-0000-0000-000000000000");

        // Act
        bool result = left.IsGreaterThan(right);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void IsGreaterThan_LeftGuidNotGreaterThanRightGuid_ReturnsFalse()
    {
        // Arrange - Use specific GUIDs where left < right
        Guid left = new Guid("00000000-0000-0000-0000-000000000000");
        Guid right = new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff");

        // Act
        bool result = left.IsGreaterThan(right);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void IsGreaterThan_EqualGuids_ReturnsFalse()
    {
        // Arrange
        Guid left = new Guid("12345678-1234-1234-1234-123456789abc");
        Guid right = new Guid("12345678-1234-1234-1234-123456789abc");

        // Act
        bool result = left.IsGreaterThan(right);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void IsGreaterThanOrEqual_LeftGuidGreaterThanRightGuid_ReturnsTrue()
    {
        // Arrange - Use specific GUIDs where left > right
        Guid left = new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff");
        Guid right = new Guid("00000000-0000-0000-0000-000000000000");

        // Act
        bool result = left.IsGreaterThanOrEqual(right);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void IsGreaterThanOrEqual_EqualGuids_ReturnsTrue()
    {
        // Arrange
        Guid left = new Guid("12345678-1234-1234-1234-123456789abc");
        Guid right = new Guid("12345678-1234-1234-1234-123456789abc");

        // Act
        bool result = left.IsGreaterThanOrEqual(right);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void IsGreaterThanOrEqual_LeftGuidLessThanRightGuid_ReturnsFalse()
    {
        // Arrange - Use specific GUIDs where left < right
        Guid left = new Guid("00000000-0000-0000-0000-000000000000");
        Guid right = new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff");

        // Act
        bool result = left.IsGreaterThanOrEqual(right);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void IsLessThan_LeftGuidLessThanRightGuid_ReturnsTrue()
    {
        // Arrange - Use specific GUIDs where left < right
        Guid left = new Guid("00000000-0000-0000-0000-000000000000");
        Guid right = new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff");

        // Act
        bool result = left.IsLessThan(right);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void IsLessThan_LeftGuidNotLessThanRightGuid_ReturnsFalse()
    {
        // Arrange - Use specific GUIDs where left > right
        Guid left = new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff");
        Guid right = new Guid("00000000-0000-0000-0000-000000000000");

        // Act
        bool result = left.IsLessThan(right);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void IsLessThan_EqualGuids_ReturnsFalse()
    {
        // Arrange
        Guid left = new Guid("12345678-1234-1234-1234-123456789abc");
        Guid right = new Guid("12345678-1234-1234-1234-123456789abc");

        // Act
        bool result = left.IsLessThan(right);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void IsLessThanOrEqual_LeftGuidLessThanRightGuid_ReturnsTrue()
    {
        // Arrange - Use specific GUIDs where left < right
        Guid left = new Guid("00000000-0000-0000-0000-000000000000");
        Guid right = new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff");

        // Act
        bool result = left.IsLessThanOrEqual(right);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void IsLessThanOrEqual_EqualGuids_ReturnsTrue()
    {
        // Arrange
        Guid left = new Guid("12345678-1234-1234-1234-123456789abc");
        Guid right = new Guid("12345678-1234-1234-1234-123456789abc");

        // Act
        bool result = left.IsLessThanOrEqual(right);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void IsLessThanOrEqual_LeftGuidGreaterThanRightGuid_ReturnsFalse()
    {
        // Arrange - Use specific GUIDs where left > right
        Guid left = new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff");
        Guid right = new Guid("00000000-0000-0000-0000-000000000000");

        // Act
        bool result = left.IsLessThanOrEqual(right);

        // Assert
        Assert.That(result, Is.False);
    }
}