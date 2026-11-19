using Microsoft.EntityFrameworkCore;
using ProductsApi.Data;
using ProductsApi.Dtos;
using ProductsApi.Services;

namespace ProductsApi.Tests;

// Unit tests: the service against a throw-away in-memory database
public class ProductServiceTests
{
    private static ProductService CreateService()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ProductService(new AppDbContext(options));
    }

    private static ProductRequest Request(string name) => new()
    {
        Name = name,
        Price = 10m,
        Stock = 1
    };

    [Fact]
    public async Task Create_SetsIdAndTimestamps()
    {
        var service = CreateService();

        var created = await service.CreateAsync(Request("Pen"));

        Assert.True(created.Id > 0);
        Assert.Equal(created.CreatedAt, created.UpdatedAt);
    }

    [Fact]
    public async Task GetAll_ReturnsRequestedPage()
    {
        var service = CreateService();
        for (var i = 1; i <= 5; i++)
        {
            await service.CreateAsync(Request($"Item {i}"));
        }

        var page2 = await service.GetAllAsync(page: 2, pageSize: 2);

        Assert.Equal(2, page2.Count);
        Assert.Equal("Item 3", page2[0].Name);
        Assert.Equal("Item 4", page2[1].Name);
    }

    [Fact]
    public async Task Update_MissingProduct_ReturnsNull()
    {
        var service = CreateService();

        var result = await service.UpdateAsync(123, Request("Nothing"));

        Assert.Null(result);
    }

    [Fact]
    public async Task Delete_ExistingProduct_RemovesIt()
    {
        var service = CreateService();
        var created = await service.CreateAsync(Request("Temp"));

        var deleted = await service.DeleteAsync(created.Id);
        var fetched = await service.GetByIdAsync(created.Id);

        Assert.True(deleted);
        Assert.Null(fetched);
    }
}
