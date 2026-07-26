using SystemUptimeTracker.Common.Constants;

namespace SystemUptimeTracker.Tests.Common;

[TestFixture(Category = "Unit")]
public class CacheKeyConstantsTests
{
    [Test]
    public void GetUserRightsKey_WhenAccountIdContainsWhitespace_ReturnsNormalizedKey()
    {
        string result = CacheKeyConstants.UserRights.GetUserRightsKey(" AbC ");

        Assert.That(result, Is.EqualTo("UserRights:Account:abc"));
    }

    [Test]
    public void GetUserRightsKey_WhenAccountIdIsWhitespace_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => CacheKeyConstants.UserRights.GetUserRightsKey(" "));
    }
}
