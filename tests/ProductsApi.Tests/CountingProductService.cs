using ProductsApi.Dtos;
using ProductsApi.Services;

namespace ProductsApi.Tests;

// A fake "database-backed" service that counts how often it is really called,
// so tests can tell whether an answer came from the cache or from the inner service.
public class CountingProductService : IProductService
{
    private int _nextId = 1;

    public List<ProductResponse> Products { get; } = new();
    public int GetAllCalls { get; private set; }
    public int GetByIdCalls { get; private set; }

    public Task<IReadOnlyList<ProductResponse>> GetAllAsync(int page, int pageSize)
    {
        GetAllCalls++;
        IReadOnlyList<ProductResponse> items = Products
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();
        return Task.FromResult(items);
    }

    public Task<ProductResponse?> GetByIdAsync(int id)
    {
        GetByIdCalls++;
        return Task.FromResult(Products.FirstOrDefault(p => p.Id == id));
    }

    public Task<ProductResponse> CreateAsync(ProductRequest request)
    {
        var now = DateTime.UtcNow;
        var product = new ProductResponse(
            _nextId++, request.Name, request.Description, request.Price, request.Stock, now, now);
        Products.Add(product);
        return Task.FromResult(product);
    }

    public Task<ProductResponse?> UpdateAsync(int id, ProductRequest request)
    {
        var index = Products.FindIndex(p => p.Id == id);
        if (index < 0)
        {
            return Task.FromResult<ProductResponse?>(null);
        }

        var updated = Products[index] with
        {
            Name = request.Name,
            Description = request.Description,
            Price = request.Price,
            Stock = request.Stock,
            UpdatedAt = DateTime.UtcNow
        };
        Products[index] = updated;
        return Task.FromResult<ProductResponse?>(updated);
    }

    public Task<bool> DeleteAsync(int id)
    {
        var removed = Products.RemoveAll(p => p.Id == id) > 0;
        return Task.FromResult(removed);
    }
}
