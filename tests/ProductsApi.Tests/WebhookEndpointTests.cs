using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProductsApi.Data;
using ProductsApi.Dtos;
using ProductsApi.Entities;
using ProductsApi.Webhooks;

namespace ProductsApi.Tests;

// Integration tests: real HTTP requests, real signature check, real SQLite transactions
public class WebhookEndpointTests : IClassFixture<ProductsApiFactory>
{
    private const string EventsUrl = "/api/webhooks/events";

    private readonly ProductsApiFactory _factory;
    private readonly HttpClient _client;

    public WebhookEndpointTests(ProductsApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ---------- helpers ----------

    private static string NewEventId() => "evt_" + Guid.NewGuid().ToString("N");

    private static string EventJson(string eventId, int productId, int delta) =>
        $"{{\"id\":\"{eventId}\",\"type\":\"stock.adjusted\",\"data\":{{\"productId\":{productId},\"delta\":{delta}}}}}";

    // Builds a request signed exactly like a real sender would: HMAC over "<timestamp>.<raw bytes>"
    private static HttpRequestMessage BuildRequest(
        string json,
        string secret = ProductsApiFactory.WebhookSecret,
        long? timestamp = null,
        string? signatureOverride = null)
    {
        var body = Encoding.UTF8.GetBytes(json);
        var ts = (timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds()).ToString();

        var request = new HttpRequestMessage(HttpMethod.Post, EventsUrl)
        {
            Content = new ByteArrayContent(body)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Add("X-Webhook-Timestamp", ts);
        request.Headers.Add(
            "X-Webhook-Signature",
            signatureOverride ?? WebhookSignatureVerifier.ComputeSignatureHeader(secret, ts, body));
        return request;
    }

    private async Task<int> SeedProductAsync(int stock)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var product = new Product { Name = "Webhook test item", Price = 1m, Stock = stock, CreatedAt = now, UpdatedAt = now };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product.Id;
    }

    private async Task<int> GetStockAsync(int productId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Products.AsNoTracking().Where(p => p.Id == productId).Select(p => p.Stock).SingleAsync();
    }

    private async Task<int> CountEventsAsync(string eventId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.WebhookEvents.CountAsync(e => e.EventId == eventId);
    }

    private async Task<int> CountOutboxAsync(string eventId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.OutboxMessages.CountAsync(m => m.EventId == eventId);
    }

    private static async Task<string> ErrorCodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("error").GetString()!;
    }

    // ---------- accepted ----------

    [Fact]
    public async Task ValidEvent_Returns201_ChangesStockAndCreatesOutboxMessage()
    {
        var productId = await SeedProductAsync(stock: 10);
        var eventId = NewEventId();

        var response = await _client.SendAsync(BuildRequest(EventJson(eventId, productId, 5)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(15, await GetStockAsync(productId));
        Assert.Equal(1, await CountEventsAsync(eventId));
        Assert.Equal(1, await CountOutboxAsync(eventId));
    }

    [Fact]
    public async Task Signature_IsCheckedAgainstTheExactBytes_NotAReserializedJson()
    {
        var productId = await SeedProductAsync(stock: 1);
        var eventId = NewEventId();
        // Odd spacing and field order: a server that re-serialized the JSON would compute another signature
        var json = $"{{  \"data\" : {{ \"delta\":2 , \"productId\":{productId} }},\n  \"type\":\"stock.adjusted\",  \"id\":\"{eventId}\"  }}";

        var response = await _client.SendAsync(BuildRequest(json));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(3, await GetStockAsync(productId));
    }

    [Fact]
    public async Task NegativeDelta_DecreasesStock()
    {
        var productId = await SeedProductAsync(stock: 10);

        var response = await _client.SendAsync(BuildRequest(EventJson(NewEventId(), productId, -4)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(6, await GetStockAsync(productId));
    }

    // ---------- invalid ----------

    [Fact]
    public async Task WrongSignature_Returns401_AndChangesNothing()
    {
        var productId = await SeedProductAsync(stock: 10);
        var eventId = NewEventId();

        var response = await _client.SendAsync(
            BuildRequest(EventJson(eventId, productId, 5), secret: "not-the-real-secret"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("invalid_signature", await ErrorCodeAsync(response));
        Assert.Equal(10, await GetStockAsync(productId));
        Assert.Equal(0, await CountEventsAsync(eventId));
    }

    [Fact]
    public async Task TamperedBody_Returns401()
    {
        var productId = await SeedProductAsync(stock: 10);
        var eventId = NewEventId();
        var request = BuildRequest(EventJson(eventId, productId, 5));
        // Same headers, different body: an attacker changing the amount in transit
        request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(EventJson(eventId, productId, 500)));

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(10, await GetStockAsync(productId));
    }

    [Fact]
    public async Task MissingSignatureHeader_Returns401()
    {
        var productId = await SeedProductAsync(stock: 10);
        var request = BuildRequest(EventJson(NewEventId(), productId, 5));
        request.Headers.Remove("X-Webhook-Signature");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("invalid_signature", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task MalformedSignatureHeader_Returns401()
    {
        var productId = await SeedProductAsync(stock: 10);

        var response = await _client.SendAsync(
            BuildRequest(EventJson(NewEventId(), productId, 5), signatureOverride: "sha256=not-hex"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("invalid_signature", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task MalformedTimestampHeader_Returns401()
    {
        var productId = await SeedProductAsync(stock: 10);
        var request = BuildRequest(EventJson(NewEventId(), productId, 5));
        request.Headers.Remove("X-Webhook-Timestamp");
        request.Headers.Add("X-Webhook-Timestamp", "yesterday");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("invalid_timestamp", await ErrorCodeAsync(response));
    }

    // ---------- stale ----------

    [Fact]
    public async Task StaleTimestamp_WithAValidSignature_Returns401Stale_AndChangesNothing()
    {
        var productId = await SeedProductAsync(stock: 10);
        var eventId = NewEventId();
        var oneHourAgo = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds();

        var response = await _client.SendAsync(
            BuildRequest(EventJson(eventId, productId, 5), timestamp: oneHourAgo));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("stale_timestamp", await ErrorCodeAsync(response));
        Assert.Equal(10, await GetStockAsync(productId));
        Assert.Equal(0, await CountEventsAsync(eventId));
    }

    // ---------- malformed (authenticated, but bad content) ----------

    [Fact]
    public async Task MalformedJson_WithAValidSignature_Returns400()
    {
        var response = await _client.SendAsync(BuildRequest("{ this is not json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("malformed_json", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task EmptyBody_WithAValidSignature_Returns400()
    {
        var response = await _client.SendAsync(BuildRequest(string.Empty));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task InvalidSchema_Returns400WithProblems_AndChangesNothing()
    {
        var productId = await SeedProductAsync(stock: 10);

        var response = await _client.SendAsync(BuildRequest(EventJson(NewEventId(), productId, 0)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_schema", await ErrorCodeAsync(response));
        Assert.Equal(10, await GetStockAsync(productId));
    }

    [Fact]
    public async Task UnsupportedEventType_Returns400()
    {
        var json = "{\"id\":\"" + NewEventId() + "\",\"type\":\"order.created\",\"data\":{\"productId\":1,\"delta\":1}}";

        var response = await _client.SendAsync(BuildRequest(json));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- business rules ----------

    [Fact]
    public async Task UnknownProduct_Returns422_AndTheEventIsNotRecorded()
    {
        var eventId = NewEventId();

        var response = await _client.SendAsync(BuildRequest(EventJson(eventId, productId: 987654, delta: 5)));

        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        Assert.Equal(0, await CountEventsAsync(eventId));
        Assert.Equal(0, await CountOutboxAsync(eventId));
    }

    [Fact]
    public async Task InsufficientStock_Returns422_AndNothingChanges()
    {
        var productId = await SeedProductAsync(stock: 2);
        var eventId = NewEventId();

        var response = await _client.SendAsync(BuildRequest(EventJson(eventId, productId, -5)));

        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        Assert.Equal(2, await GetStockAsync(productId));
        Assert.Equal(0, await CountEventsAsync(eventId));
    }

    // ---------- repeated ----------

    [Fact]
    public async Task RepeatedDelivery_Returns200Duplicate_AndAppliesTheEffectOnce()
    {
        var productId = await SeedProductAsync(stock: 10);
        var eventId = NewEventId();
        var json = EventJson(eventId, productId, 5);

        var first = await _client.SendAsync(BuildRequest(json));
        var second = await _client.SendAsync(BuildRequest(json));
        var third = await _client.SendAsync(BuildRequest(json));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.OK, third.StatusCode);
        Assert.Equal(15, await GetStockAsync(productId));
        Assert.Equal(1, await CountEventsAsync(eventId));
        Assert.Equal(1, await CountOutboxAsync(eventId));
    }

    [Fact]
    public async Task ReplayedRequestWithAFreshSignatureButTheSameEventId_IsStillADuplicate()
    {
        var productId = await SeedProductAsync(stock: 10);
        var eventId = NewEventId();

        await _client.SendAsync(BuildRequest(EventJson(eventId, productId, 5)));
        var retryAtALaterSecond = await _client.SendAsync(
            BuildRequest(EventJson(eventId, productId, 5), timestamp: DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 1));

        Assert.Equal(HttpStatusCode.OK, retryAtALaterSecond.StatusCode);
        Assert.Equal(15, await GetStockAsync(productId));
    }

    // ---------- concurrent ----------

    [Fact]
    public async Task ConcurrentDeliveriesOfTheSameEvent_AreAppliedExactlyOnce()
    {
        var productId = await SeedProductAsync(stock: 100);
        var eventId = NewEventId();
        var json = EventJson(eventId, productId, 7);

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 10).Select(_ => _client.SendAsync(BuildRequest(json))));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(9, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(107, await GetStockAsync(productId));
        Assert.Equal(1, await CountEventsAsync(eventId));
        Assert.Equal(1, await CountOutboxAsync(eventId));
    }

    [Fact]
    public async Task ConcurrentDifferentEventsForTheSameProduct_AllApply_WithoutLostUpdates()
    {
        var productId = await SeedProductAsync(stock: 0);

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 10).Select(_ => _client.SendAsync(BuildRequest(EventJson(NewEventId(), productId, 3)))));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        Assert.Equal(30, await GetStockAsync(productId));
    }

    // ---------- other ----------

    [Fact]
    public async Task WebhookChangesAreVisibleThroughTheCachedProductEndpoint()
    {
        var productId = await SeedProductAsync(stock: 10);

        var before = await _client.GetFromJsonAsync<ProductResponse>($"/api/products/{productId}");
        await _client.SendAsync(BuildRequest(EventJson(NewEventId(), productId, 5)));
        var after = await _client.GetFromJsonAsync<ProductResponse>($"/api/products/{productId}");

        Assert.Equal(10, before!.Stock);
        Assert.Equal(15, after!.Stock);
    }

    [Fact]
    public async Task WithoutASigningSecret_TheEndpointFailsClosedWith503()
    {
        using var factory = new ProductsApiFactory().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.Configure<WebhookOptions>(options => options.Secret = string.Empty)));
        var client = factory.CreateClient();

        var response = await client.SendAsync(BuildRequest(EventJson(NewEventId(), 1, 1)));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
