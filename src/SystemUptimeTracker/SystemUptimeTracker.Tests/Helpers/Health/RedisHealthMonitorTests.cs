using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SystemUptimeTracker.Api.Helpers.Health;
using SystemUptimeTracker.Api.Models.ApplicationSettings;
using StackExchange.Redis;
using System.Reflection;

namespace SystemUptimeTracker.Tests.Helpers.Health;

[TestFixture(Category = "Unit")]
public class RedisHealthMonitorTests
{
    private IDatabase _database;
    private RedisHealthState _healthState;
    private ILogger<RedisHealthMonitor> _logger;
    private IConnectionMultiplexer _multiplexer;
    private IOptions<RedisHealthMonitorOptions> _options;

    [SetUp]
    public void SetUp()
    {
        _multiplexer = Substitute.For<IConnectionMultiplexer>();
        _database = Substitute.For<IDatabase>();
        _multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(_database);
        _healthState = new RedisHealthState(Substitute.For<ILogger<RedisHealthState>>());
        _logger = Substitute.For<ILogger<RedisHealthMonitor>>();
        _options = Substitute.For<IOptions<RedisHealthMonitorOptions>>();
        _options.Value.Returns(new RedisHealthMonitorOptions
        {
            IntervalSeconds = 1
        });
    }

    [TearDown]
    public void TearDown()
    {
        _multiplexer.Dispose();
    }

    [Test]
    public async Task ExecuteAsync_WhenPingFails_MarksRedisUnavailable()
    {
        var redisFailed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _healthState.OnFailed += () => redisFailed.TrySetResult();
        _database.PingAsync(Arg.Any<CommandFlags>()).Throws(new Exception("Redis unavailable"));
        var monitor = new RedisHealthMonitor(_multiplexer, _healthState, _options, _logger);

        await monitor.StartAsync(CancellationToken.None);
        await redisFailed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await monitor.StopAsync(CancellationToken.None);

        Assert.That(_healthState.IsAvailable, Is.False);
    }

    [Test]
    public async Task ExecuteAsync_WhenRedisRecovers_MarksRedisAvailable()
    {
        var invocationCount = 0;
        var redisFailed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var redisRecovered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _healthState.OnFailed += () => redisFailed.TrySetResult();
        _healthState.OnRecovered += () => redisRecovered.TrySetResult();
        _database.PingAsync(Arg.Any<CommandFlags>()).Returns(_ =>
        {
            invocationCount++;
            if (invocationCount == 1)
            {
                throw new Exception("Redis unavailable");
            }

            return TimeSpan.FromMilliseconds(5);
        });

        var monitor = new RedisHealthMonitor(_multiplexer, _healthState, _options, _logger);

        await monitor.StartAsync(CancellationToken.None);
        await redisFailed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await redisRecovered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await monitor.StopAsync(CancellationToken.None);

        Assert.That(_healthState.IsAvailable, Is.True);
    }

    [Test]
    public void Constructor_WhenIntervalIsNotPositive_ClampsToOneSecond()
    {
        _options.Value.Returns(new RedisHealthMonitorOptions
        {
            IntervalSeconds = 0
        });

        var monitor = new RedisHealthMonitor(_multiplexer, _healthState, _options, _logger);
        var interval = (TimeSpan)typeof(RedisHealthMonitor)
            .GetField("_interval", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(monitor)!;

        Assert.That(interval, Is.EqualTo(TimeSpan.FromSeconds(1)));
        Assert.That(GetWarningLogMessages(), Has.Some.Contains("Using the 1s minimum instead"));
    }

    private IEnumerable<string> GetWarningLogMessages()
    {
        return _logger.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(ILogger.Log))
            .Where(call => call.GetArguments()[0] is LogLevel.Warning)
            .Select(call => call.GetArguments()[2]?.ToString() ?? string.Empty);
    }
}
