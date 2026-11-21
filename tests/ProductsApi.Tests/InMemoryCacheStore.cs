using ProductsApi.Caching;

namespace ProductsApi.Tests;

// A fake cache for tests: same behaviour as Redis for what we use (values, expiry, counters),
// with no server needed.
public class InMemoryCacheStore : ICacheStore
{
    private sealed record Entry(string Value, DateTimeOffset? ExpiresAt, TimeSpan? Ttl);

    private readonly object _lock = new();
    private readonly Dictionary<string, Entry> _entries = new();

    public IReadOnlyList<string> Keys
    {
        get
        {
            lock (_lock)
            {
                RemoveExpired();
                return _entries.Keys.ToList();
            }
        }
    }

    // The TTL that was passed when the key was written
    public TimeSpan? TtlSetFor(string key)
    {
        lock (_lock)
        {
            return _entries.TryGetValue(key, out var entry) ? entry.Ttl : null;
        }
    }

    public Task<string?> GetAsync(string key)
    {
        lock (_lock)
        {
            RemoveExpired();
            return Task.FromResult<string?>(_entries.TryGetValue(key, out var entry) ? entry.Value : null);
        }
    }

    public Task SetAsync(string key, string value, TimeSpan ttl)
    {
        lock (_lock)
        {
            _entries[key] = new Entry(value, DateTimeOffset.UtcNow + ttl, ttl);
        }

        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key)
    {
        lock (_lock)
        {
            _entries.Remove(key);
        }

        return Task.CompletedTask;
    }

    public Task<long> IncrementAsync(string key, TimeSpan? expiryIfNew = null)
    {
        lock (_lock)
        {
            RemoveExpired();

            if (_entries.TryGetValue(key, out var existing))
            {
                var next = long.Parse(existing.Value) + 1;
                _entries[key] = existing with { Value = next.ToString() };
                return Task.FromResult(next);
            }

            DateTimeOffset? expiresAt = expiryIfNew is null ? null : DateTimeOffset.UtcNow + expiryIfNew.Value;
            _entries[key] = new Entry("1", expiresAt, expiryIfNew);
            return Task.FromResult(1L);
        }
    }

    public Task<TimeSpan?> GetTimeToLiveAsync(string key)
    {
        lock (_lock)
        {
            RemoveExpired();

            if (_entries.TryGetValue(key, out var entry) && entry.ExpiresAt is not null)
            {
                return Task.FromResult<TimeSpan?>(entry.ExpiresAt.Value - DateTimeOffset.UtcNow);
            }

            return Task.FromResult<TimeSpan?>(null);
        }
    }

    private void RemoveExpired()
    {
        var now = DateTimeOffset.UtcNow;
        var expired = _entries
            .Where(pair => pair.Value.ExpiresAt is not null && pair.Value.ExpiresAt <= now)
            .Select(pair => pair.Key)
            .ToList();

        foreach (var key in expired)
        {
            _entries.Remove(key);
        }
    }
}
