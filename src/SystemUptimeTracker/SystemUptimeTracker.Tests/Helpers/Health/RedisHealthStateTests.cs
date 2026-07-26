using Microsoft.Extensions.Logging;
using NSubstitute;
using SystemUptimeTracker.Api.Helpers.Health;

namespace SystemUptimeTracker.Tests.Helpers.Health;

[TestFixture(Category = "Unit")]
public class RedisHealthStateTests
{
    [Test]
    public void Constructor_WhenInitialAvailabilityIsFalse_StartsUnavailable()
    {
        var state = new RedisHealthState(Substitute.For<ILogger<RedisHealthState>>(), initialAvailability: false);

        Assert.That(state.IsAvailable, Is.False);
    }

    [Test]
    public void Constructor_WhenInitialAvailabilityIsTrue_StartsAvailable()
    {
        var state = new RedisHealthState(Substitute.For<ILogger<RedisHealthState>>(), initialAvailability: true);

        Assert.That(state.IsAvailable, Is.True);
    }
}
