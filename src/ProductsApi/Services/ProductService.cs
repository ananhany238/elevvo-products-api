using Microsoft.EntityFrameworkCore;
using ProductsApi.Data;
using ProductsApi.Dtos;
using ProductsApi.Entities;

namespace ProductsApi.Services;

public class ProductService : IProductService
{
    private readonly AppDbContext _db;

    public ProductService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<ProductResponse>> GetAllAsync(int page, int pageSize)
    {
        var products = await _db.Products
            .AsNoTracking()
            .OrderBy(p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return products.Select(ToResponse).ToList();
    }

    public async Task<ProductResponse?> GetByIdAsync(int id)
    {
        var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        return product is null ? null : ToResponse(product);
    }

    public async Task<ProductResponse> CreateAsync(ProductRequest request)
    {
        var now = DateTime.UtcNow;
        var product = new Product
        {
            Name = request.Name,
            Description = request.Description,
            Price = request.Price,
            Stock = request.Stock,
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.Products.Add(product);
        await _db.SaveChangesAsync();
        return ToResponse(product);
    }

    public async Task<ProductResponse?> UpdateAsync(int id, ProductRequest request)
    {
        var product = await _db.Products.FindAsync(id);
        if (product is null)
        {
            return null;
        }

        product.Name = request.Name;
        product.Description = request.Description;
        product.Price = request.Price;
        product.Stock = request.Stock;
        product.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return ToResponse(product);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var product = await _db.Products.FindAsync(id);
        if (product is null)
        {
            return false;
        }

        _db.Products.Remove(product);
        await _db.SaveChangesAsync();
        return true;
    }

    private static ProductResponse ToResponse(Product p) =>
        new(p.Id, p.Name, p.Description, p.Price, p.Stock, p.CreatedAt, p.UpdatedAt);
}
