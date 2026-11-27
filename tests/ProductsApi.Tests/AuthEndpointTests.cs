using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using ProductsApi.Auth;

namespace ProductsApi.Tests;

public class AuthEndpointTests : IClassFixture<ProductsApiFactory>
{
    private readonly ProductsApiFactory _factory;
    private readonly HttpClient _client;

    public AuthEndpointTests(ProductsApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private Task<HttpResponseMessage> LoginAsync(string? username, string? password) =>
        _client.PostAsJsonAsync("/api/auth/login", new { username, password });

    [Fact]
    public async Task ValidCredentials_ReturnAToken_ThatTheHubAccepts()
    {
        var response = await LoginAsync("alice", ProductsApiFactory.AlicePassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var token = body.RootElement.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.Equal("Bearer", body.RootElement.GetProperty("tokenType").GetString());

        await using var client = new TestNotificationClient(_factory, () => token);
        await client.StartAsync(); // would throw if the token were not accepted
        Assert.Equal(Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Connected, client.State);
    }

    [Fact]
    public async Task UsernameIsCaseInsensitive()
    {
        var response = await LoginAsync("ALICE", ProductsApiFactory.AlicePassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task WrongPassword_Returns401()
    {
        var response = await LoginAsync("alice", "wrong-password");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("invalid_credentials", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UnknownUser_GetsTheSame401AsAWrongPassword()
    {
        var wrongPassword = await LoginAsync("alice", "wrong-password");
        var unknownUser = await LoginAsync("mallory", "whatever");

        Assert.Equal(HttpStatusCode.Unauthorized, unknownUser.StatusCode);
        Assert.Equal(
            await wrongPassword.Content.ReadAsStringAsync(),
            await unknownUser.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task OnesUsersPassword_DoesNotWorkForAnotherUser()
    {
        var response = await LoginAsync("bob", ProductsApiFactory.AlicePassword);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MissingFields_Return400()
    {
        var response = await LoginAsync("alice", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task WithoutAJwtKey_LoginFailsClosedWith503()
    {
        using var factory = new ProductsApiFactory().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.Configure<JwtOptions>(options => options.Key = string.Empty)));

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/auth/login", new { username = "alice", password = ProductsApiFactory.AlicePassword });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
