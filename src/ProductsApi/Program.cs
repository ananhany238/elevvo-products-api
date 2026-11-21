using Microsoft.EntityFrameworkCore;
using ProductsApi.Caching;
using ProductsApi.Data;
using ProductsApi.Middleware;
using ProductsApi.Services;
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

builder.Services.Configure<RateLimitOptions>(builder.Configuration.GetSection("RateLimit"));

// The real service, wrapped by the caching decorator (cache-aside)
builder.Services.AddScoped<ProductService>();
builder.Services.AddScoped<IProductService>(sp => new CachedProductService(
    sp.GetRequiredService<ProductService>(),
    sp.GetRequiredService<ICacheStore>(),
    sp.GetRequiredService<ILogger<CachedProductService>>()));

var app = builder.Build();

// Creates the tables on first start (simple approach for this task; migrations can replace it later)
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
