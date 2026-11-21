using Microsoft.Extensions.Logging.Abstractions;
using ProductsApi.Dtos;
using ProductsApi.Services;

namespace ProductsApi.Tests;

// Unit tests for the cache-aside decorator, using a counting fake instead of the database
public class CachedProductServiceTests
{
    private static CachedProductService Create(out CountingProductService inner, out InMemoryCacheStore cache)
    {
        inner = new CountingProductService();
        cache = new InMemoryCacheStore();
        return new CachedProductService(inner, cache, NullLogger<CachedProductService>.Instance);
    }

    private static ProductRequest Request(string name) => new()
    {
        Name = name,
        Price = 10m,
        Stock = 1
    };

    [Fact]
    public async Task GetAll_SecondCallIsServedFromCache()
    {
        var service = Create(out var inner, out _);
        await inner.CreateAsync(Request("Pen"));

        var first = await service.GetAllAsync(1, 10);
        var second = await service.GetAllAsync(1, 10);

        Assert.Equal(1, inner.GetAllCalls);
        Assert.Equal(first.Count, second.Count);
        Assert.Equal("Pen", second[0].Name);
    }

    [Fact]
    public async Task GetAll_StoresTheListWithAOneHourTtl()
    {
        var service = Create(out _, out var cache);

        await service.GetAllAsync(1, 10);

        var key = Assert.Single(cache.Keys);
        Assert.StartsWith("products:list:", key);
        Assert.Equal(TimeSpan.FromHours(1), cache.TtlSetFor(key));
    }

    [Fact]
    public async Task GetAll_DifferentPagesUseDifferentCacheEntries()
    {
        var service = Create(out var inner, out _);
        await inner.CreateAsync(Request("A"));
        await inner.CreateAsync(Request("B"));

        await service.GetAllAsync(1, 1);
        await service.GetAllAsync(2, 1);

        Assert.Equal(2, inner.GetAllCalls);
    }

    [Fact]
    public async Task Create_InvalidatesTheListCache()
    {
        var service = Create(out var inner, out _);

        await service.GetAllAsync(1, 10);
        await service.CreateAsync(Request("New"));
        var list = await service.GetAllAsync(1, 10);

        Assert.Equal(2, inner.GetAllCalls);
        Assert.Single(list);
    }

    [Fact]
    public async Task Update_InvalidatesItemAndListCaches()
    {
        var service = Create(out var inner, out _);
        var created = await inner.CreateAsync(Request("Old"));

        await service.GetByIdAsync(created.Id);
        await service.GetAllAsync(1, 10);
        await service.UpdateAsync(created.Id, Request("New"));
        var byId = await service.GetByIdAsync(created.Id);
        var list = await service.GetAllAsync(1, 10);

        Assert.Equal("New", byId!.Name);
        Assert.Equal("New", list[0].Name);
        Assert.Equal(2, inner.GetByIdCalls);
        Assert.Equal(2, inner.GetAllCalls);
    }

    [Fact]
    public async Task Delete_InvalidatesItemAndListCaches()
    {
        var service = Create(out var inner, out _);
        var created = await inner.CreateAsync(Request("Temp"));

        await service.GetByIdAsync(created.Id);
        await service.GetAllAsync(1, 10);
        await service.DeleteAsync(created.Id);
        var byId = await service.GetByIdAsync(created.Id);
        var list = await service.GetAllAsync(1, 10);

        Assert.Null(byId);
        Assert.Empty(list);
    }

    [Fact]
    public async Task GetById_MissingProductIsNotCached()
    {
        var service = Create(out var inner, out _);

        await service.GetByIdAsync(99);
        await service.GetByIdAsync(99);

        Assert.Equal(2, inner.GetByIdCalls);
    }

    [Fact]
    public async Task WhenCacheIsDown_TheServiceStillWorks()
    {
        var inner = new CountingProductService();
        var service = new CachedProductService(
            inner, new ThrowingCacheStore(), NullLogger<CachedProductService>.Instance);

        var created = await service.CreateAsync(Request("Pen"));
        var list = await service.GetAllAsync(1, 10);

        Assert.Single(list);
        Assert.Equal(created.Id, list[0].Id);
    }
}
