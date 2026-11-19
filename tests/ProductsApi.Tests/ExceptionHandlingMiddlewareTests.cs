using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using ProductsApi.Middleware;

namespace ProductsApi.Tests;

// Unit tests: the middleware alone, with no server and no database
public class ExceptionHandlingMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_WhenNextThrows_Returns500WithJsonError()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new ExceptionHandlingMiddleware(
            _ => throw new InvalidOperationException("boom"),
            NullLogger<ExceptionHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Contains("unexpected error", body);
        Assert.DoesNotContain("boom", body);
    }

    [Fact]
    public async Task InvokeAsync_WhenNextSucceeds_CallsNextAndKeepsStatus()
    {
        var context = new DefaultHttpContext();
        var nextWasCalled = false;
        var middleware = new ExceptionHandlingMiddleware(
            _ =>
            {
                nextWasCalled = true;
                return Task.CompletedTask;
            },
            NullLogger<ExceptionHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.True(nextWasCalled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }
}
