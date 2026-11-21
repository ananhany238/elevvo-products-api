using Microsoft.Extensions.Options;
using ProductsApi.Caching;

namespace ProductsApi.Middleware;

// Fixed-window rate limiter whose counters live in Redis. Every API instance increments the
// same counter, so the limit holds even when the API is scaled to several instances.
public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ICacheStore _cache;
    private readonly RateLimitOptions _options;
    private readonly ILogger<RateLimitingMiddleware> _logger;

    public RateLimitingMiddleware(
        RequestDelegate next,
        ICacheStore cache,
        IOptions<RateLimitOptions> options,
        ILogger<RateLimitingMiddleware> logger)
    {
        _next = next;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Health checks must always work
        if (context.Request.Path.StartsWithSegments("/health"))
        {
            await _next(context);
            return;
        }

        // Behind a reverse proxy you would read the real client IP from forwarded headers
        var clientId = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var key = $"ratelimit:{clientId}";
        var window = TimeSpan.FromSeconds(_options.WindowSeconds);

        long count;
        try
        {
            count = await _cache.IncrementAsync(key, window);
        }
        catch (Exception ex)
        {
            // Fail open: if Redis is down, serve the request instead of taking the API down
            _logger.LogWarning(ex, "Rate limiter could not reach Redis, allowing the request");
            await _next(context);
            return;
        }

        var remaining = Math.Max(0, _options.PermitLimit - count);
        context.Response.Headers["X-RateLimit-Limit"] = _options.PermitLimit.ToString();
        context.Response.Headers["X-RateLimit-Remaining"] = remaining.ToString();

        if (count > _options.PermitLimit)
        {
            TimeSpan? timeLeft = null;
            try
            {
                timeLeft = await _cache.GetTimeToLiveAsync(key);
            }
            catch (Exception)
            {
                // Not critical: fall back to the full window below
            }

            var retryAfterSeconds = (int)Math.Ceiling((timeLeft ?? window).TotalSeconds);
            context.Response.Headers["Retry-After"] = retryAfterSeconds.ToString();
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await context.Response.WriteAsJsonAsync(new { error = "Too many requests. Try again later." });
            return;
        }

        await _next(context);
    }
}
