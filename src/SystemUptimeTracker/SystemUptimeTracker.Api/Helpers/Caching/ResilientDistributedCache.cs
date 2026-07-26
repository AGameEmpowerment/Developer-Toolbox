using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using SystemUptimeTracker.Api.Helpers.Health;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace SystemUptimeTracker.Api.Helpers.Caching;

/// <summary>
/// Provides a resilient <see cref="IDistributedCache"/> that falls back to in-memory storage when Redis is unavailable.
/// </summary>
public sealed class ResilientDistributedCache : IDistributedCache, IDisposable
{
    private static readonly object _memoryKeySentinel = new();

    private readonly IDistributedCache _distributed;
    private readonly RedisHealthState _health;
    private readonly ILogger<ResilientDistributedCache> _logger;
    private readonly IMemoryCache _memory;
    private readonly ConcurrentDictionary<string, object?> _memoryKeys = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ResilientDistributedCache"/> class.
    /// </summary>
    /// <param name="distributed">The primary distributed cache implementation.</param>
    /// <param name="memory">The in-memory fallback cache.</param>
    /// <param name="health">The Redis health state.</param>
    /// <param name="logger">The logger instance.</param>
    public ResilientDistributedCache(
        IDistributedCache distributed,
        IMemoryCache memory,
        RedisHealthState health,
        ILogger<ResilientDistributedCache> logger)
    {
        _distributed = distributed ?? throw new ArgumentNullException(nameof(distributed));
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        _health = health ?? throw new ArgumentNullException(nameof(health));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _health.OnRecovered += ClearMemoryFallback;
    }

    /// <inheritdoc />
    public byte[]? Get(string key)
    {
        if (_health.IsAvailable)
        {
            try
            {
                return _distributed.Get(key);
            }
            catch (Exception ex)
            {
                LogDistributedCacheFailure(ex, "get", key);
                _health.SetUnavailable();
            }
        }

        return _memory.TryGetValue(GetMemoryKey(key), out byte[]? bytes)
            ? bytes
            : null;
    }

    /// <inheritdoc />
    public async Task<byte[]?> GetAsync(string key, CancellationToken token = default)
    {
        if (_health.IsAvailable)
        {
            try
            {
                return await _distributed.GetAsync(key, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogDistributedCacheFailure(ex, "get async", key);
                _health.SetUnavailable();
            }
        }

        return _memory.TryGetValue(GetMemoryKey(key), out byte[]? bytes)
            ? bytes
            : null;
    }

    /// <inheritdoc />
    public void Refresh(string key)
    {
        if (_health.IsAvailable)
        {
            try
            {
                _distributed.Refresh(key);
            }
            catch (Exception ex)
            {
                LogDistributedCacheFailure(ex, "refresh", key);
                _health.SetUnavailable();
            }
        }
    }

    /// <inheritdoc />
    public async Task RefreshAsync(string key, CancellationToken token = default)
    {
        if (_health.IsAvailable)
        {
            try
            {
                await _distributed.RefreshAsync(key, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogDistributedCacheFailure(ex, "refresh async", key);
                _health.SetUnavailable();
            }
        }
    }

    /// <inheritdoc />
    public void Remove(string key)
    {
        var memoryKey = GetMemoryKey(key);
        _memory.Remove(memoryKey);
        _memoryKeys.TryRemove(memoryKey, out _);

        if (_health.IsAvailable)
        {
            try
            {
                _distributed.Remove(key);
            }
            catch (Exception ex)
            {
                LogDistributedCacheFailure(ex, "remove", key);
                _health.SetUnavailable();
            }
        }
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string key, CancellationToken token = default)
    {
        var memoryKey = GetMemoryKey(key);
        _memory.Remove(memoryKey);
        _memoryKeys.TryRemove(memoryKey, out _);

        if (_health.IsAvailable)
        {
            try
            {
                await _distributed.RemoveAsync(key, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogDistributedCacheFailure(ex, "remove async", key);
                _health.SetUnavailable();
            }
        }
    }

    /// <inheritdoc />
    public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
    {
        if (_health.IsAvailable)
        {
            try
            {
                _distributed.Set(key, value, options);
                return;
            }
            catch (Exception ex)
            {
                LogDistributedCacheFailure(ex, "set", key);
                _health.SetUnavailable();
            }
        }

        _memory.Set(GetMemoryKey(key), value, CreateMemoryOptions(options));
        _memoryKeys.TryAdd(GetMemoryKey(key), _memoryKeySentinel);
    }

    /// <inheritdoc />
    public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
    {
        if (_health.IsAvailable)
        {
            try
            {
                await _distributed.SetAsync(key, value, options, token).ConfigureAwait(false);
                return;
            }
            catch (Exception ex)
            {
                LogDistributedCacheFailure(ex, "set async", key);
                _health.SetUnavailable();
            }
        }

        _memory.Set(GetMemoryKey(key), value, CreateMemoryOptions(options));
        _memoryKeys.TryAdd(GetMemoryKey(key), _memoryKeySentinel);
    }

    /// <summary>
    /// Detaches the Redis recovery callback.
    /// </summary>
    public void Dispose()
    {
        _health.OnRecovered -= ClearMemoryFallback;
    }

    private static string GetMemoryKey(string key)
    {
        return $"fallback:{key}";
    }

    private static MemoryCacheEntryOptions CreateMemoryOptions(DistributedCacheEntryOptions options)
    {
        var cacheEntryOptions = new MemoryCacheEntryOptions();

        if (options.AbsoluteExpirationRelativeToNow.HasValue)
        {
            cacheEntryOptions.SetAbsoluteExpiration(options.AbsoluteExpirationRelativeToNow.Value);
        }

        if (options.AbsoluteExpiration.HasValue)
        {
            cacheEntryOptions.SetAbsoluteExpiration(options.AbsoluteExpiration.Value);
        }

        if (options.SlidingExpiration.HasValue)
        {
            cacheEntryOptions.SetSlidingExpiration(options.SlidingExpiration.Value);
        }

        return cacheEntryOptions;
    }

    private void LogDistributedCacheFailure(Exception ex, string operation, string key)
    {
        _logger.LogWarning(
            ex,
            "Distributed cache {Operation} failed, switching to memory for key {KeyFingerprint}",
            operation,
            CreateKeyFingerprint(key));
    }

    private static string CreateKeyFingerprint(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return "unknown";
        }

        var separatorIndex = key.IndexOf(':');
        var category = separatorIndex > 0 ? key[..separatorIndex] : "uncategorized";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

        return $"{category}:{hash[..12]}";
    }

    private void ClearMemoryFallback()
    {
        try
        {
            _logger.LogInformation("Redis recovered: clearing in-memory fallback cache ({Count} entries).", _memoryKeys.Count);

            foreach (var key in _memoryKeys.Keys)
            {
                try
                {
                    _memory.Remove(key);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed removing memory fallback key {KeyFingerprint}", CreateKeyFingerprint(key));
                }
            }

            _memoryKeys.Clear();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed clearing in-memory fallback cache.");
        }
    }
}
