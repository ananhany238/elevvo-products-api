using ProductsApi.Caching;

namespace ProductsApi.Tests;

// Simulates Redis being down: every call fails
public class ThrowingCacheStore : ICacheStore
{
    private const string Message = "Cache is down";

    public Task<string?> GetAsync(string key) => throw new InvalidOperationException(Message);

    public Task SetAsync(string key, string value, TimeSpan ttl) => throw new InvalidOperationException(Message);

    public Task RemoveAsync(string key) => throw new InvalidOperationException(Message);

    public Task<long> IncrementAsync(string key, TimeSpan? expiryIfNew = null) =>
        throw new InvalidOperationException(Message);

    public Task<TimeSpan?> GetTimeToLiveAsync(string key) => throw new InvalidOperationException(Message);
}
