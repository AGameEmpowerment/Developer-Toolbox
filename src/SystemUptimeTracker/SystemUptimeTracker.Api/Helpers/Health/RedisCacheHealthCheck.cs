using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace SystemUptimeTracker.Api.Helpers.Health;

/// <summary>
/// Reports the current Redis connectivity state for the health-check endpoint.
/// </summary>
public class RedisCacheHealthCheck : IHealthCheck
{
    private readonly ILogger<RedisCacheHealthCheck> _logger;
    private readonly IConnectionMultiplexer _mux;
    private readonly RedisHealthState _state;

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisCacheHealthCheck"/> class.
    /// </summary>
    /// <param name="mux">The Redis connection multiplexer.</param>
    /// <param name="state">The Redis health state.</param>
    /// <param name="logger">The logger instance.</param>
    public RedisCacheHealthCheck(
        IConnectionMultiplexer mux,
        RedisHealthState state,
        ILogger<RedisCacheHealthCheck> logger)
    {
        _mux = mux ?? throw new ArgumentNullException(nameof(mux));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Redis health check requested. IsAvailable={IsAvailable}, IsConnected={IsConnected}",
                _state.IsAvailable,
                _mux.IsConnected);
        }

        if (!_state.IsAvailable)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Redis is marked unavailable by the health monitor."));
        }

        if (!_mux.IsConnected)
        {
            return Task.FromResult(HealthCheckResult.Degraded("Redis is marked available but the multiplexer is not connected."));
        }

        return Task.FromResult(HealthCheckResult.Healthy("Redis is available and connected."));
    }
}
