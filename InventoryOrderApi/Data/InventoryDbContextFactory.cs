using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace InventoryOrderApi.Data;

public sealed class InventoryDbContextFactory : IDesignTimeDbContextFactory<InventoryDbContext>
{
    public InventoryDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Inventory")
            ?? "Host=localhost;Database=inventory;Username=inventory";
        var options = new DbContextOptionsBuilder<InventoryDbContext>().UseNpgsql(connection).Options;
        return new InventoryDbContext(options);
    }
}
