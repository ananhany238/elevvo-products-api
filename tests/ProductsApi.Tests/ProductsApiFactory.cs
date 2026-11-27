using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using ProductsApi.Auth;
using ProductsApi.Caching;
using ProductsApi.Data;
using ProductsApi.Outbox;
using ProductsApi.Realtime;
using ProductsApi.Webhooks;

namespace ProductsApi.Tests;

// Starts the real API in memory with:
//  - a throw-away SQLite database file instead of PostgreSQL (a real relational engine, so unique
//    indexes, transactions and concurrent writers behave like in production)
//  - an in-memory cache instead of Redis
//  - a known webhook signing secret, JWT key, two demo users and a simulation key
//  - no background outbox worker (tests run the outbox processor themselves)
public class ProductsApiFactory : WebApplicationFactory<Program>
{
    public const string WebhookSecret = "test-webhook-secret-0123456789abcdef";
    public const string JwtKey = "test-jwt-signing-key-0123456789-abcdefghij";
    public const string AlicePassword = "alice-test-password-1";
    public const string BobPassword = "bob-test-password-2";
    public const string SimulationKey = "test-simulation-key-abcdef";

    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"products-tests-{Guid.NewGuid():N}.db");

    // Exposed so tests can look inside the cache
    public InMemoryCacheStore Cache { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // EF Core 9+ registers the Npgsql setup in more than one service.
            // Remove every DbContext-options registration for AppDbContext so
            // PostgreSQL and SQLite never end up registered together.
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
                options.UseSqlite($"Data Source={_databasePath}"));

            // Replace Redis with the in-memory cache
            services.RemoveAll<ICacheStore>();
            services.AddSingleton<ICacheStore>(Cache);

            // No background polling during tests (tests run the processors themselves)
            var workers = services
                .Where(d => d.ServiceType == typeof(IHostedService)
                            && (d.ImplementationType == typeof(OutboxWorker)
                                || d.ImplementationType == typeof(NotificationRetryWorker)))
                .ToList();
            foreach (var worker in workers)
            {
                services.Remove(worker);
            }

            services.Configure<WebhookOptions>(options => options.Secret = WebhookSecret);

            services.Configure<JwtOptions>(options => options.Key = JwtKey);
            services.Configure<DemoAuthOptions>(options =>
            {
                options.Users["alice"] = AlicePassword;
                options.Users["bob"] = BobPassword;
            });
            services.Configure<NotificationOptions>(options =>
            {
                options.SimulationKey = SimulationKey;
                options.RetryBaseDelayMs = 100;
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            SqliteConnection.ClearAllPools();
            try
            {
                File.Delete(_databasePath);
            }
            catch (IOException)
            {
                // A leftover temp file is harmless
            }
        }
    }
}
