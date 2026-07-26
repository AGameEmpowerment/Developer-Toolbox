using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SystemUptimeTracker.Api.Helpers.Health;
using StackExchange.Redis;

namespace SystemUptimeTracker.Tests.Helpers.Health;

[TestFixture(Category = "Unit")]
public class RedisCacheHealthCheckTests
{
    private RedisCacheHealthCheck _healthCheck;
    private RedisHealthState _healthState;
    private IConnectionMultiplexer _multiplexer;

    [SetUp]
    public void SetUp()
    {
        _multiplexer = Substitute.For<IConnectionMultiplexer>();
        _healthState = new RedisHealthState(Substitute.For<ILogger<RedisHealthState>>());
        _healthCheck = new RedisCacheHealthCheck(
            _multiplexer,
            _healthState,
            Substitute.For<ILogger<RedisCacheHealthCheck>>());
    }

    [TearDown]
    public void TearDown()
    {
        _multiplexer.Dispose();
    }

    [Test]
    public async Task CheckHealthAsync_WhenRedisIsMarkedUnavailable_ReturnsUnhealthy()
    {
        _healthState.SetUnavailable();

        var result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.That(result.Status, Is.EqualTo(HealthStatus.Unhealthy));
    }

    [Test]
    public async Task CheckHealthAsync_WhenRedisIsDisconnected_ReturnsDegraded()
    {
        _multiplexer.IsConnected.Returns(false);

        var result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.That(result.Status, Is.EqualTo(HealthStatus.Degraded));
    }

    [Test]
    public async Task CheckHealthAsync_WhenRedisIsAvailableAndConnected_ReturnsHealthy()
    {
        _multiplexer.IsConnected.Returns(true);

        var result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.That(result.Status, Is.EqualTo(HealthStatus.Healthy));
    }
}
