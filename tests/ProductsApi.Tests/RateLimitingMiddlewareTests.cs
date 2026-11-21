using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProductsApi.Caching;
using ProductsApi.Middleware;

namespace ProductsApi.Tests;

// Unit tests for the rate limiter: no server, no Redis
public class RateLimitingMiddlewareTests
{
    private sealed class Counter
    {
        public int Calls { get; set; }
    }

    private static RateLimitingMiddleware CreateMiddleware(ICacheStore cache, int limit, Counter counter)
    {
        return new RateLimitingMiddleware(
            _ =>
            {
                counter.Calls++;
                return Task.CompletedTask;
            },
            cache,
            Options.Create(new RateLimitOptions { PermitLimit = limit, WindowSeconds = 60 }),
            NullLogger<RateLimitingMiddleware>.Instance);
    }

    private static DefaultHttpContext CreateContext(string ip = "10.0.0.1", string path = "/api/products")
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        return context;
    }

    [Fact]
    public async Task AllowsRequestsUpToTheLimit_ThenReturns429WithRetryAfter()
    {
        var counter = new Counter();
        var middleware = CreateMiddleware(new InMemoryCacheStore(), limit: 3, counter);

        for (var i = 0; i < 3; i++)
        {
            var allowed = CreateContext();
            await middleware.InvokeAsync(allowed);
            Assert.Equal(StatusCodes.Status200OK, allowed.Response.StatusCode);
        }

        var blocked = CreateContext();
        await middleware.InvokeAsync(blocked);

        Assert.Equal(StatusCodes.Status429TooManyRequests, blocked.Response.StatusCode);
        Assert.Equal(3, counter.Calls);
        Assert.True(int.Parse(blocked.Response.Headers["Retry-After"].ToString()) > 0);
    }

    [Fact]
    public async Task AddsRemainingRequestsHeader()
    {
        var middleware = CreateMiddleware(new InMemoryCacheStore(), limit: 3, new Counter());
        var context = CreateContext();

        await middleware.InvokeAsync(context);

        Assert.Equal("3", context.Response.Headers["X-RateLimit-Limit"].ToString());
        Assert.Equal("2", context.Response.Headers["X-RateLimit-Remaining"].ToString());
    }

    [Fact]
    public async Task DifferentClientsHaveSeparateLimits()
    {
        var middleware = CreateMiddleware(new InMemoryCacheStore(), limit: 1, new Counter());

        var clientA = CreateContext("10.0.0.1");
        var clientB = CreateContext("10.0.0.2");
        var clientAAgain = CreateContext("10.0.0.1");

        await middleware.InvokeAsync(clientA);
        await middleware.InvokeAsync(clientB);
        await middleware.InvokeAsync(clientAAgain);

        Assert.Equal(StatusCodes.Status200OK, clientA.Response.StatusCode);
        Assert.Equal(StatusCodes.Status200OK, clientB.Response.StatusCode);
        Assert.Equal(StatusCodes.Status429TooManyRequests, clientAAgain.Response.StatusCode);
    }

    [Fact]
    public async Task HealthEndpointIsNeverRateLimited()
    {
        var counter = new Counter();
        var middleware = CreateMiddleware(new InMemoryCacheStore(), limit: 1, counter);

        for (var i = 0; i < 5; i++)
        {
            var context = CreateContext(path: "/health");
            await middleware.InvokeAsync(context);
            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        }

        Assert.Equal(5, counter.Calls);
    }

    [Fact]
    public async Task WhenCacheIsDown_TheRequestIsAllowed()
    {
        var counter = new Counter();
        var middleware = CreateMiddleware(new ThrowingCacheStore(), limit: 1, counter);
        var context = CreateContext();

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(1, counter.Calls);
    }
}
