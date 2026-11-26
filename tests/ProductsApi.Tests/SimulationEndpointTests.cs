using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using ProductsApi.Realtime;

namespace ProductsApi.Tests;

public class SimulationEndpointTests : IClassFixture<ProductsApiFactory>, IAsyncLifetime
{
    private const string Url = "/api/dev/simulate-status";

    private readonly ProductsApiFactory _factory;
    private readonly HttpClient _client;
    private readonly List<TestNotificationClient> _clients = new();

    public SimulationEndpointTests(ProductsApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync()
    {
        _factory.Services.GetRequiredService<PendingNotificationTracker>().Clear();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        foreach (var client in _clients)
        {
            await client.DisposeAsync();
        }
    }

    private static HttpRequestMessage Build(string json, string? apiKey = ProductsApiFactory.SimulationKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        if (apiKey is not null)
        {
            request.Headers.Add("X-Api-Key", apiKey);
        }

        return request;
    }

    private static string Body(string userId, string orderId = "ord_1", string status = "shipped") =>
        $"{{\"userId\":\"{userId}\",\"orderId\":\"{orderId}\",\"status\":\"{status}\"}}";

    [Fact]
    public async Task ValidRequest_Returns202_AndOnlyTheTargetUserReceivesIt()
    {
        var alice = new TestNotificationClient(_factory, () => TestTokens.For(_factory, "alice"));
        var bob = new TestNotificationClient(_factory, () => TestTokens.For(_factory, "bob"));
        _clients.Add(alice);
        _clients.Add(bob);
        await alice.StartAsync();
        await bob.StartAsync();

        var response = await _client.SendAsync(Build(Body("bob", "ord_77", "delivered")));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.True(await TestWait.UntilAsync(() => bob.Raw.Count == 1));
        await Task.Delay(300);
        Assert.Equal("ord_77", bob.Raw[0].OrderId);
        Assert.Equal("delivered", bob.Raw[0].Status);
        Assert.Empty(alice.Raw);
    }

    [Fact]
    public async Task WithoutTheApiKey_Returns401()
    {
        var response = await _client.SendAsync(Build(Body("alice"), apiKey: null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WithTheWrongApiKey_Returns401()
    {
        var response = await _client.SendAsync(Build(Body("alice"), apiKey: "guess"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AUsersJwt_IsNotTheSimulationKey()
    {
        var request = Build(Body("bob"), apiKey: TestTokens.For(_factory, "alice"));

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WhenNoKeyIsConfigured_TheEndpointIsDisabledAndLooksLikeItDoesNotExist()
    {
        using var factory = new ProductsApiFactory().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.Configure<NotificationOptions>(options => options.SimulationKey = string.Empty)));

        var response = await factory.CreateClient().SendAsync(Build(Body("alice")));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("{}")]
    [InlineData("{\"userId\":\"alice\",\"orderId\":\"ord_1\",\"status\":\"exploded\"}")]
    [InlineData("{\"userId\":\"alice\",\"orderId\":\"bad id!\",\"status\":\"shipped\"}")]
    [InlineData("{\"userId\":\"user:bob\",\"orderId\":\"ord_1\",\"status\":\"shipped\"}")]
    public async Task InvalidRequests_Return400(string json)
    {
        var response = await _client.SendAsync(Build(json));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UnknownUser_Returns400()
    {
        var response = await _client.SendAsync(Build(Body("mallory")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("unknown_user", await response.Content.ReadAsStringAsync());
    }
}
