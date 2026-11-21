using StackExchange.Redis;

namespace ProductsApi.Caching;

public class RedisCacheStore : ICacheStore
{
    // INCR and PEXPIRE run as one atomic step inside Redis, so a crash can never leave
    // a counter without an expiry (which would block a client forever).
    private const string IncrementWithExpiryScript = @"
local count = redis.call('INCR', KEYS[1])
if count == 1 then
  redis.call('PEXPIRE', KEYS[1], ARGV[1])
end
return count";

    private readonly IConnectionMultiplexer _redis;

    public RedisCacheStore(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    // Fails fast when Redis is down, instead of waiting for a timeout on every call
    private IDatabase Db
    {
        get
        {
            if (!_redis.IsConnected)
            {
                throw new InvalidOperationException("Redis is not connected");
            }

            return _redis.GetDatabase();
        }
    }

    public async Task<string?> GetAsync(string key)
    {
        RedisValue value = await Db.StringGetAsync(key);
        return value.HasValue ? value.ToString() : null;
    }

    public async Task SetAsync(string key, string value, TimeSpan ttl)
    {
        await Db.StringSetAsync(key, value, ttl);
    }

    public async Task RemoveAsync(string key)
    {
        await Db.KeyDeleteAsync(key);
    }

    public async Task<long> IncrementAsync(string key, TimeSpan? expiryIfNew = null)
    {
        var db = Db;

        if (expiryIfNew is null)
        {
            return await db.StringIncrementAsync(key);
        }

        var result = await db.ScriptEvaluateAsync(
            IncrementWithExpiryScript,
            new RedisKey[] { key },
            new RedisValue[] { (long)expiryIfNew.Value.TotalMilliseconds });

        return (long)result;
    }

    public async Task<TimeSpan?> GetTimeToLiveAsync(string key)
    {
        return await Db.KeyTimeToLiveAsync(key);
    }
}
