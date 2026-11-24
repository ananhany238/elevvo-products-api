using System.Text.Json;
using ProductsApi.Caching;
using ProductsApi.Dtos;

namespace ProductsApi.Services;

// Cache-aside decorator around the real ProductService:
//   read  -> look in the cache first; on a miss ask the inner service and store the result (1 hour TTL)
//   write -> do the write, then invalidate what it made stale
// If the cache fails for any reason, the API keeps working by reading from the database.
public class CachedProductService : IProductService
{
    public static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    // Lists are cached under a key that contains a "version" number (see ProductCacheKeys).
    // Bumping the version makes every old list key unreachable at once (they expire after 1 hour).
    private readonly IProductService _inner;
    private readonly ICacheStore _cache;
    private readonly ILogger<CachedProductService> _logger;

    public CachedProductService(IProductService inner, ICacheStore cache, ILogger<CachedProductService> logger)
    {
        _inner = inner;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ProductResponse>> GetAllAsync(int page, int pageSize)
    {
        string? key = null;

        try
        {
            var version = await _cache.GetAsync(ProductCacheKeys.ListVersionKey) ?? "0";
            key = $"products:list:v{version}:page{page}:size{pageSize}";

            var cached = await _cache.GetAsync(key);
            if (cached is not null)
            {
                var items = JsonSerializer.Deserialize<List<ProductResponse>>(cached);
                if (items is not null)
                {
                    _logger.LogInformation("Cache HIT {Key}", key);
                    return items;
                }
            }

            _logger.LogInformation("Cache MISS {Key}", key);
        }
        catch (JsonException ex)
        {
            // Unreadable cache entry: treat it as a miss, it will be overwritten below
            _logger.LogWarning(ex, "Cache entry {Key} could not be read", key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache unavailable, reading from the database");
            key = null;
        }

        var result = await _inner.GetAllAsync(page, pageSize);

        if (key is not null)
        {
            await TrySetAsync(key, JsonSerializer.Serialize(result));
        }

        return result;
    }

    public async Task<ProductResponse?> GetByIdAsync(int id)
    {
        var key = ProductCacheKeys.ItemKey(id);

        try
        {
            var cached = await _cache.GetAsync(key);
            if (cached is not null)
            {
                var item = JsonSerializer.Deserialize<ProductResponse>(cached);
                if (item is not null)
                {
                    _logger.LogInformation("Cache HIT {Key}", key);
                    return item;
                }
            }

            _logger.LogInformation("Cache MISS {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache unavailable, reading from the database");
        }

        var product = await _inner.GetByIdAsync(id);

        // A missing product is not cached, so a product created later is found straight away
        if (product is not null)
        {
            await TrySetAsync(key, JsonSerializer.Serialize(product));
        }

        return product;
    }

    public async Task<ProductResponse> CreateAsync(ProductRequest request)
    {
        var created = await _inner.CreateAsync(request);
        await InvalidateListsAsync();
        return created;
    }

    public async Task<ProductResponse?> UpdateAsync(int id, ProductRequest request)
    {
        var updated = await _inner.UpdateAsync(id, request);

        if (updated is not null)
        {
            await TryRemoveAsync(ProductCacheKeys.ItemKey(id));
            await InvalidateListsAsync();
        }

        return updated;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var deleted = await _inner.DeleteAsync(id);

        if (deleted)
        {
            await TryRemoveAsync(ProductCacheKeys.ItemKey(id));
            await InvalidateListsAsync();
        }

        return deleted;
    }

    private async Task InvalidateListsAsync()
    {
        try
        {
            await _cache.IncrementAsync(ProductCacheKeys.ListVersionKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not invalidate the product list cache");
        }
    }

    private async Task TrySetAsync(string key, string json)
    {
        try
        {
            await _cache.SetAsync(key, json, CacheTtl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not write {Key} to the cache", key);
        }
    }

    private async Task TryRemoveAsync(string key)
    {
        try
        {
            await _cache.RemoveAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not remove {Key} from the cache", key);
        }
    }
}
