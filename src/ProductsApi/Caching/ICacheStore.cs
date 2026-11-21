namespace ProductsApi.Caching;

// A tiny abstraction over the cache. The rest of the app talks to this interface,
// so Redis can be swapped for a fake in tests.
public interface ICacheStore
{
    Task<string?> GetAsync(string key);

    Task SetAsync(string key, string value, TimeSpan ttl);

    Task RemoveAsync(string key);

    // Adds 1 to a counter and returns the new value. When the counter is created
    // (first call) and expiryIfNew is given, the counter will expire after that time.
    Task<long> IncrementAsync(string key, TimeSpan? expiryIfNew = null);

    Task<TimeSpan?> GetTimeToLiveAsync(string key);
}
