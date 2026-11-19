using System.Net;
using System.Net.Http.Json;
using ProductsApi.Dtos;

namespace ProductsApi.Tests;

// Integration tests: real HTTP requests through the whole pipeline (routing, validation, controller, service, database)
public class ProductsEndpointsTests : IClassFixture<ProductsApiFactory>
{
    private readonly HttpClient _client;

    public ProductsEndpointsTests(ProductsApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    private static ProductRequest ValidRequest(string name = "Keyboard") => new()
    {
        Name = name,
        Description = "Mechanical keyboard",
        Price = 49.99m,
        Stock = 10
    };

    private async Task<ProductResponse> CreateProductAsync(string name = "Keyboard")
    {
        var response = await _client.PostAsJsonAsync("/api/products", ValidRequest(name));
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<ProductResponse>();
        return created!;
    }

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("OK", body);
    }

    [Fact]
    public async Task Create_ValidProduct_Returns201WithLocation()
    {
        var response = await _client.PostAsJsonAsync("/api/products", ValidRequest("Mouse"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var created = await response.Content.ReadFromJsonAsync<ProductResponse>();
        Assert.Equal("Mouse", created!.Name);
        Assert.True(created.Id > 0);
    }

    [Fact]
    public async Task Create_InvalidProduct_Returns400()
    {
        var invalid = new ProductRequest { Name = "", Price = 0, Stock = -1 };

        var response = await _client.PostAsJsonAsync("/api/products", invalid);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetById_ExistingProduct_ReturnsIt()
    {
        var created = await CreateProductAsync("Monitor");

        var response = await _client.GetAsync($"/api/products/{created.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var fetched = await response.Content.ReadFromJsonAsync<ProductResponse>();
        Assert.Equal("Monitor", fetched!.Name);
    }

    [Fact]
    public async Task GetById_MissingProduct_Returns404()
    {
        var response = await _client.GetAsync("/api/products/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_ReturnsCreatedProducts()
    {
        await CreateProductAsync("ListItemOne");
        await CreateProductAsync("ListItemTwo");

        var response = await _client.GetAsync("/api/products?page=1&pageSize=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await response.Content.ReadFromJsonAsync<List<ProductResponse>>();
        Assert.Contains(list!, p => p.Name == "ListItemOne");
        Assert.Contains(list!, p => p.Name == "ListItemTwo");
    }

    [Fact]
    public async Task GetAll_InvalidPaging_Returns400()
    {
        var response = await _client.GetAsync("/api/products?page=0&pageSize=500");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_ExistingProduct_ReturnsUpdatedValues()
    {
        var created = await CreateProductAsync("Old name");
        var update = ValidRequest("New name");
        update.Price = 99.50m;

        var response = await _client.PutAsJsonAsync($"/api/products/{created.Id}", update);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<ProductResponse>();
        Assert.Equal("New name", updated!.Name);
        Assert.Equal(99.50m, updated.Price);
    }

    [Fact]
    public async Task Update_MissingProduct_Returns404()
    {
        var response = await _client.PutAsJsonAsync("/api/products/999999", ValidRequest());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_ExistingProduct_Returns204ThenGetReturns404()
    {
        var created = await CreateProductAsync("To delete");

        var deleteResponse = await _client.DeleteAsync($"/api/products/{created.Id}");
        var getResponse = await _client.GetAsync($"/api/products/{created.Id}");

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task Delete_MissingProduct_Returns404()
    {
        var response = await _client.DeleteAsync("/api/products/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
