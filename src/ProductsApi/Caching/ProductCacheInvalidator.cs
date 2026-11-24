namespace ProductsApi.Caching;

// Used by code that changes products WITHOUT going through CachedProductService
// (for example the webhook processor), so cached copies never go stale.
public class ProductCacheInvalidator
{
    private readonly ICacheStore _cache;
    private readonly ILogger<ProductCacheInvalidator> _logger;

    public ProductCacheInvalidator(ICacheStore cache, ILogger<ProductCacheInvalidator> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task InvalidateProductAsync(int productId)
    {
        try
        {
            await _cache.RemoveAsync(ProductCacheKeys.ItemKey(productId));
            await _cache.IncrementAsync(ProductCacheKeys.ListVersionKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not invalidate the cache for product {ProductId}", productId);
        }
    }
}
