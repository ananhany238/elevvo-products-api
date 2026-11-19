using Microsoft.AspNetCore.Mvc;
using ProductsApi.Controllers;
using ProductsApi.Dtos;
using ProductsApi.Services;

namespace ProductsApi.Tests;

// Unit tests: the controller alone, with a hand-written fake instead of the real service
public class ProductsControllerTests
{
    private sealed class FakeProductService : IProductService
    {
        public ProductResponse? ProductToReturn { get; set; }
        public bool DeleteResult { get; set; }

        public Task<IReadOnlyList<ProductResponse>> GetAllAsync(int page, int pageSize) =>
            Task.FromResult<IReadOnlyList<ProductResponse>>(new List<ProductResponse>());

        public Task<ProductResponse?> GetByIdAsync(int id) => Task.FromResult(ProductToReturn);

        public Task<ProductResponse> CreateAsync(ProductRequest request) =>
            Task.FromResult(ProductToReturn!);

        public Task<ProductResponse?> UpdateAsync(int id, ProductRequest request) =>
            Task.FromResult(ProductToReturn);

        public Task<bool> DeleteAsync(int id) => Task.FromResult(DeleteResult);
    }

    [Fact]
    public async Task GetById_WhenServiceReturnsNull_ReturnsNotFound()
    {
        var controller = new ProductsController(new FakeProductService { ProductToReturn = null });

        var result = await controller.GetById(1);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetAll_WithInvalidPaging_ReturnsBadRequest()
    {
        var controller = new ProductsController(new FakeProductService());

        var result = await controller.GetAll(page: 0, pageSize: 20);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Delete_WhenServiceReturnsFalse_ReturnsNotFound()
    {
        var controller = new ProductsController(new FakeProductService { DeleteResult = false });

        var result = await controller.Delete(1);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Delete_WhenServiceReturnsTrue_ReturnsNoContent()
    {
        var controller = new ProductsController(new FakeProductService { DeleteResult = true });

        var result = await controller.Delete(1);

        Assert.IsType<NoContentResult>(result);
    }
}
