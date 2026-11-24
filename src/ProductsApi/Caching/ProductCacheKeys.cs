namespace ProductsApi.Caching;

// One place for the cache key formats, so everything that touches product data agrees on them
public static class ProductCacheKeys
{
    public const string ListVersionKey = "products:list:version";

    public static string ItemKey(int id) => $"products:item:{id}";
}
