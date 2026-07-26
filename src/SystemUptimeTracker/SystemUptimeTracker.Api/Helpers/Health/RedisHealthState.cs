namespace SystemUptimeTracker.Api.Helpers.Health;

/// <summary>
/// Tracks Redis availability so the resilient cache can switch between Redis and in-memory fallback paths.
/// </summary>
public class RedisHealthState
{
    private int _available;
    private readonly ILogger<RedisHealthState> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisHealthState"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="initialAvailability">Whether Redis should be treated as available before the first health check runs.</param>
    public RedisHealthState(ILogger<RedisHealthState> logger, bool initialAvailability = true)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _available = initialAvailability ? 1 : 0;
    }

    /// <summary>
    /// Gets a value indicating whether Redis is currently considered available.
    /// </summary>
    public bool IsAvailable => Volatile.Read(ref _available) == 1;

    /// <summary>
    /// Raised when Redis becomes healthy again.
    /// </summary>
    public event Action? OnRecovered;

    /// <summary>
    /// Raised when Redis transitions to an unavailable state.
    /// </summary>
    public event Action? OnFailed;

    /// <summary>
    /// Marks Redis as available.
    /// </summary>
    public void SetAvailable()
    {
        var previousValue = Interlocked.Exchange(ref _available, 1);
        if (previousValue == 0)
        {
            RaiseSafely(OnRecovered, nameof(OnRecovered));
        }
    }

    /// <summary>
    /// Marks Redis as unavailable.
    /// </summary>
    public void SetUnavailable()
    {
        var previousValue = Interlocked.Exchange(ref _available, 0);
        if (previousValue == 1)
        {
            RaiseSafely(OnFailed, nameof(OnFailed));
        }
    }

    private void RaiseSafely(Action? handlers, string eventName)
    {
        if (handlers is null)
        {
            return;
        }

        foreach (Action handler in handlers.GetInvocationList())
        {
            try
            {
                handler();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis health state transition handler failed: {EventName}", eventName);
            }
        }
    }
}
