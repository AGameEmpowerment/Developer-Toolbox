using Microsoft.Extensions.Options;
using SystemUptimeTracker.Api.Models.ApplicationSettings;
using StackExchange.Redis;

namespace SystemUptimeTracker.Api.Helpers.Health;

/// <summary>
/// Background service that pings Redis and keeps <see cref="RedisHealthState"/> in sync with connectivity.
/// </summary>
public class RedisHealthMonitor : BackgroundService
{
    private readonly TimeSpan _interval;
    private readonly ILogger<RedisHealthMonitor> _logger;
    private readonly IConnectionMultiplexer _mux;
    private readonly RedisHealthState _state;

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisHealthMonitor"/> class.
    /// </summary>
    /// <param name="mux">The Redis connection multiplexer.</param>
    /// <param name="state">The Redis health state.</param>
    /// <param name="options">The configured health-monitor options.</param>
    /// <param name="logger">The logger instance.</param>
    public RedisHealthMonitor(
        IConnectionMultiplexer mux,
        RedisHealthState state,
        IOptions<RedisHealthMonitorOptions> options,
        ILogger<RedisHealthMonitor> logger)
    {
        _mux = mux ?? throw new ArgumentNullException(nameof(mux));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        int configuredIntervalSeconds = options?.Value?.IntervalSeconds ?? 5;
        if (configuredIntervalSeconds < 1)
        {
            _logger.LogWarning(
                "Redis health monitor interval {ConfiguredIntervalSeconds}s is invalid. Using the 1s minimum instead.",
                configuredIntervalSeconds);
            configuredIntervalSeconds = 1;
        }

        _interval = TimeSpan.FromSeconds(configuredIntervalSeconds);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting Redis health monitor (interval {IntervalSeconds}s).", _interval.TotalSeconds);

        await PerformHealthCheckAsync().ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_interval, stoppingToken).ConfigureAwait(false);
                await PerformHealthCheckAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Redis health monitor stopped.");
    }

    private async Task PerformHealthCheckAsync()
    {
        try
        {
            var database = _mux.GetDatabase();
            var pong = await database.PingAsync().ConfigureAwait(false);
            if (!_state.IsAvailable)
            {
                _logger.LogInformation("Redis ping successful ({PingMs}ms). Marking available.", pong.TotalMilliseconds);
                _state.SetAvailable();
            }
        }
        catch (Exception ex)
        {
            if (_state.IsAvailable)
            {
                _logger.LogWarning(ex, "Redis health check failed; marking unavailable.");
                _state.SetUnavailable();
            }
        }
    }
}
