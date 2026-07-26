using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SystemUptimeTracker.Api.Helpers.Caching;
using SystemUptimeTracker.Api.Helpers.Health;
using System.Reflection;
using System.Text;

namespace SystemUptimeTracker.Tests.Helpers.Caching;

[TestFixture(Category = "Unit")]
public class ResilientDistributedCacheTests
{
    private ResilientDistributedCache _cache;
    private IDistributedCache _distributedCache;
    private RedisHealthState _healthState;
    private ILogger<ResilientDistributedCache> _logger;
    private IMemoryCache _memoryCache;

    [SetUp]
    public void SetUp()
    {
        _distributedCache = Substitute.For<IDistributedCache>();
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
        _healthState = new RedisHealthState(Substitute.For<ILogger<RedisHealthState>>());
        _logger = Substitute.For<ILogger<ResilientDistributedCache>>();

        _cache = new ResilientDistributedCache(_distributedCache, _memoryCache, _healthState, _logger);
    }

    [TearDown]
    public void TearDown()
    {
        _cache.Dispose();
        _memoryCache.Dispose();
    }

    [Test]
    public void Get_WhenRedisIsHealthy_ReturnsDistributedValue()
    {
        var key = "test-key";
        var expectedValue = Encoding.UTF8.GetBytes("distributed-value");
        _distributedCache.Get(key).Returns(expectedValue);

        var result = _cache.Get(key);

        Assert.That(result, Is.EqualTo(expectedValue));
        _distributedCache.Received(1).Get(key);
    }

    [Test]
    public void Get_WhenDistributedCacheThrows_ReturnsMemoryFallbackAndMarksRedisUnavailable()
    {
        var key = "test-key";
        var fallbackValue = Encoding.UTF8.GetBytes("fallback-value");

        _distributedCache.Get(key)!.Throws(new Exception("Redis unavailable"));
        _memoryCache.Set("fallback:test-key", fallbackValue);

        var result = _cache.Get(key);

        Assert.That(result, Is.EqualTo(fallbackValue));
        Assert.That(_healthState.IsAvailable, Is.False);
    }

    [Test]
    public void Get_WhenDistributedCacheThrows_LogsSanitizedKeyFingerprint()
    {
        const string KEY = "UserRights:Account:account-12345";

        _distributedCache.Get(KEY)!.Throws(new Exception("Redis unavailable"));

        _cache.Get(KEY);

        Assert.That(GetWarningLogMessages(), Has.Some.Contains("Distributed cache get failed"));
        Assert.That(GetWarningLogMessages(), Has.Some.Contains("UserRights:"));
        Assert.That(GetWarningLogMessages(), Has.None.Contain("account-12345"));
    }

    [Test]
    public void Get_WhenRedisStartsUnavailable_UsesMemoryFallbackWithoutCallingDistributedCache()
    {
        var key = "test-key";
        var fallbackValue = Encoding.UTF8.GetBytes("fallback-value");
        var unavailableState = new RedisHealthState(Substitute.For<ILogger<RedisHealthState>>(), initialAvailability: false);

        using var cache = new ResilientDistributedCache(_distributedCache, _memoryCache, unavailableState, _logger);
        _memoryCache.Set("fallback:test-key", fallbackValue);

        var result = cache.Get(key);

        Assert.That(result, Is.EqualTo(fallbackValue));
        _distributedCache.DidNotReceive().Get(key);
    }

    [Test]
    public void Set_WhenDistributedCacheThrows_WritesValueToMemoryFallback()
    {
        var key = "test-key";
        var value = Encoding.UTF8.GetBytes("payload");
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
        };

        _distributedCache.When(x => x.Set(key, value, options)).Throw(new Exception("Redis unavailable"));

        _cache.Set(key, value, options);

        Assert.That(_memoryCache.Get<byte[]>("fallback:test-key"), Is.EqualTo(value));
        Assert.That(_healthState.IsAvailable, Is.False);
    }

    [Test]
    public void Remove_WhenRedisIsUnavailable_RemovesOnlyMemoryFallback()
    {
        var key = "test-key";
        _memoryCache.Set("fallback:test-key", Encoding.UTF8.GetBytes("payload"));
        _healthState.SetUnavailable();

        _cache.Remove(key);

        Assert.That(_memoryCache.Get<byte[]>("fallback:test-key"), Is.Null);
        _distributedCache.DidNotReceive().Remove(Arg.Any<string>());
    }

    [Test]
    public void SetAvailable_WhenFallbackEntriesExist_ClearsTrackedMemoryEntries()
    {
        var key = "test-key";
        var value = Encoding.UTF8.GetBytes("payload");
        var options = new DistributedCacheEntryOptions();

        _distributedCache.When(x => x.Set(key, value, options)).Throw(new Exception("Redis unavailable"));
        _cache.Set(key, value, options);

        Assert.That(_memoryCache.Get<byte[]>("fallback:test-key"), Is.Not.Null);

        _healthState.SetAvailable();

        Assert.That(_memoryCache.Get<byte[]>("fallback:test-key"), Is.Null);
    }

    private IEnumerable<string> GetWarningLogMessages()
    {
        return _logger.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(ILogger.Log))
            .Where(call => call.GetArguments()[0] is LogLevel.Warning)
            .Select(call => call.GetArguments()[2]?.ToString() ?? string.Empty);
    }
}
