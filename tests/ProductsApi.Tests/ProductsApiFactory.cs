using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProductsApi.Caching;
using ProductsApi.Data;

namespace ProductsApi.Tests;

// Starts the real API in memory, but swaps PostgreSQL for an in-memory database
// and Redis for an in-memory cache, so tests need no external services.
public class ProductsApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = Guid.NewGuid().ToString();

    // Exposed so tests can look inside the cache
    public InMemoryCacheStore Cache { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // EF Core 9+ registers the Npgsql setup in more than one service.
            // Remove every DbContext-options registration for AppDbContext so
            // PostgreSQL and the in-memory provider never end up registered together.
            var toRemove = services
                .Where(d => d.ServiceType.IsGenericType
                            && d.ServiceType.GenericTypeArguments.Contains(typeof(AppDbContext))
                            && d.ServiceType.Name.Contains("DbContextOptions"))
                .ToList();

            foreach (var descriptor in toRemove)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName));

            // Replace Redis with the in-memory cache
            services.RemoveAll<ICacheStore>();
            services.AddSingleton<ICacheStore>(Cache);
        });
    }
}
