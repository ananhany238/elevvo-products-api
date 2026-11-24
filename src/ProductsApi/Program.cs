using Microsoft.EntityFrameworkCore;
using ProductsApi.Caching;
using ProductsApi.Data;
using ProductsApi.Middleware;
using ProductsApi.Outbox;
using ProductsApi.Services;
using ProductsApi.Webhooks;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// Redis connection. It is created the first time something needs it, and the API
// still starts (and keeps working without the cache) if Redis is down.
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var connectionString = builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379";
    var options = ConfigurationOptions.Parse(connectionString);
    options.AbortOnConnectFail = false;
    options.ConnectTimeout = 2000;
    options.SyncTimeout = 1000;
    options.AsyncTimeout = 1000;
    return ConnectionMultiplexer.Connect(options);
});
builder.Services.AddSingleton<ICacheStore, RedisCacheStore>();
builder.Services.AddSingleton<ProductCacheInvalidator>();

builder.Services.Configure<RateLimitOptions>(builder.Configuration.GetSection("RateLimit"));

// The real service, wrapped by the caching decorator (cache-aside)
builder.Services.AddScoped<ProductService>();
builder.Services.AddScoped<IProductService>(sp => new CachedProductService(
    sp.GetRequiredService<ProductService>(),
    sp.GetRequiredService<ICacheStore>(),
    sp.GetRequiredService<ILogger<CachedProductService>>()));

// Webhooks (Task 3): signature verification, idempotent processing and the transactional outbox
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<WebhookOptions>(builder.Configuration.GetSection("Webhook"));
builder.Services.Configure<OutboxOptions>(builder.Configuration.GetSection("Outbox"));
builder.Services.AddSingleton<WebhookSignatureVerifier>();
builder.Services.AddScoped<WebhookEventProcessor>();
builder.Services.AddSingleton<IExternalNotifier, LoggingExternalNotifier>();
builder.Services.AddScoped<OutboxProcessor>();
builder.Services.AddHostedService<OutboxWorker>();

var app = builder.Build();

// Creates the tables on first start. Note: EnsureCreated does nothing if the database already
// has tables, so after adding new tables to the model, reset the dev database (docker compose down -v).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<RateLimitingMiddleware>();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "OK" }));

app.Run();

// Makes the Program class visible to WebApplicationFactory in the test project
public partial class Program { }
