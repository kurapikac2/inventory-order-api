using InventoryOrderApi.Domain;
using Microsoft.EntityFrameworkCore;

namespace InventoryOrderApi.Data;

public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<Category>().HasIndex(c => c.Name).IsUnique();
        builder.Entity<Product>().HasIndex(p => p.Sku).IsUnique();
        builder.Entity<Product>().Property(p => p.Price).HasPrecision(18, 2);
        builder.Entity<Product>().ToTable(t => t.HasCheckConstraint("CK_Product_Stock", "\"OnHand\" >= 0 AND \"Reserved\" >= 0 AND \"OnHand\" >= \"Reserved\""));
        builder.Entity<Order>().Property(o => o.Status).HasConversion<string>();
        builder.Entity<Order>().HasMany(o => o.Lines).WithOne().HasForeignKey(l => l.OrderId);
        builder.Entity<OrderLine>().Property(l => l.UnitPrice).HasPrecision(18, 2);
        builder.Entity<OrderLine>().HasIndex(l => new { l.OrderId, l.ProductId }).IsUnique();
    }
}
