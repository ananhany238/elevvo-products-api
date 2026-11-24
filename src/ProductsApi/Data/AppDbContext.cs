using Microsoft.EntityFrameworkCore;
using ProductsApi.Entities;

namespace ProductsApi.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<WebhookEvent> WebhookEvents => Set<WebhookEvent>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(entity =>
        {
            entity.Property(p => p.Name).IsRequired().HasMaxLength(200);
            entity.Property(p => p.Price).HasPrecision(18, 2);
        });

        modelBuilder.Entity<WebhookEvent>(entity =>
        {
            entity.Property(e => e.EventId).IsRequired().HasMaxLength(100);
            entity.Property(e => e.EventType).IsRequired().HasMaxLength(100);
            entity.HasIndex(e => e.EventId).IsUnique();
        });

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.Property(m => m.EventId).IsRequired().HasMaxLength(100);
            entity.Property(m => m.Type).IsRequired().HasMaxLength(100);
            entity.Property(m => m.Payload).IsRequired().HasMaxLength(2000);
            entity.Property(m => m.LastError).HasMaxLength(500);
            entity.HasIndex(m => m.EventId).IsUnique();
        });
    }
}
