using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using ProductsApi.Middleware;

namespace ProductsApi.Tests;

// Integration test: the limiter is wired into the real pipeline
public class RateLimitingEndpointsTests
{
    [Fact]
    public async Task ExceedingTheLimit_Returns429WithRetryAfter()
    {
        using var factory = new ProductsApiFactory().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.Configure<RateLimitOptions>(options => options.PermitLimit = 3)));
        var client = factory.CreateClient();

        for (var i = 0; i < 3; i++)
        {
            var allowed = await client.GetAsync("/api/products");
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        var blocked = await client.GetAsync("/api/products");

        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.NotNull(blocked.Headers.RetryAfter);
    }
}
