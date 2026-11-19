using Microsoft.EntityFrameworkCore;
using ProductsApi.Data;
using ProductsApi.Middleware;
using ProductsApi.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddScoped<IProductService, ProductService>();

var app = builder.Build();

// Creates the tables on first start (simple approach for this task; migrations can replace it later)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "OK" }));

app.Run();

// Makes the Program class visible to WebApplicationFactory in the test project
public partial class Program { }
