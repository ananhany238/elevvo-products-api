using ProductsApi.Dtos;

namespace ProductsApi.Services;

public interface IProductService
{
    Task<IReadOnlyList<ProductResponse>> GetAllAsync(int page, int pageSize);
    Task<ProductResponse?> GetByIdAsync(int id);
    Task<ProductResponse> CreateAsync(ProductRequest request);
    Task<ProductResponse?> UpdateAsync(int id, ProductRequest request);
    Task<bool> DeleteAsync(int id);
}
