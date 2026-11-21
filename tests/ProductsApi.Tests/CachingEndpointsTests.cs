using System.Net;
using System.Net.Http.Json;
using ProductsApi.Dtos;

namespace ProductsApi.Tests;

// Integration tests: real HTTP requests, real decorator, fake in-memory cache
public class CachingEndpointsTests : IClassFixture<ProductsApiFactory>
{
    private readonly ProductsApiFactory _factory;
    private readonly HttpClient _client;

    public CachingEndpointsTests(ProductsApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static ProductRequest Request(string name) => new()
    {
        Name = name,
        Description = "Test product",
        Price = 5m,
        Stock = 1
    };

    private async Task<ProductResponse> CreateProductAsync(string name)
    {
        var response = await _client.PostAsJsonAsync("/api/products", Request(name));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProductResponse>())!;
    }

    private async Task<List<ProductResponse>> GetListAsync()
    {
        var response = await _client.GetAsync("/api/products?page=1&pageSize=100");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<ProductResponse>>())!;
    }

    [Fact]
    public async Task GetAll_PopulatesTheCache()
    {
        await GetListAsync();

        Assert.Contains(_factory.Cache.Keys, key => key.StartsWith("products:list:"));
    }

    [Fact]
    public async Task Create_AfterAListWasCached_TheNewProductShowsUpInTheNextList()
    {
        await GetListAsync();

        await CreateProductAsync("FreshItem");
        var list = await GetListAsync();

        Assert.Contains(list, p => p.Name == "FreshItem");
    }

    [Fact]
    public async Task Update_AfterAnItemWasCached_ReturnsTheNewValue()
    {
        var created = await CreateProductAsync("BeforeUpdate");
        await _client.GetAsync($"/api/products/{created.Id}");

        var update = Request("AfterUpdate");
        var putResponse = await _client.PutAsJsonAsync($"/api/products/{created.Id}", update);
        putResponse.EnsureSuccessStatusCode();

        var fetched = await _client.GetFromJsonAsync<ProductResponse>($"/api/products/{created.Id}");
        Assert.Equal("AfterUpdate", fetched!.Name);
    }

    [Fact]
    public async Task Delete_AfterAnItemWasCached_ReturnsNotFound()
    {
        var created = await CreateProductAsync("ToRemove");
        await _client.GetAsync($"/api/products/{created.Id}");

        await _client.DeleteAsync($"/api/products/{created.Id}");

        var response = await _client.GetAsync($"/api/products/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
