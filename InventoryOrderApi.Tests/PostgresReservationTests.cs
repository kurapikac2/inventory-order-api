using InventoryOrderApi.Data;
using InventoryOrderApi.Domain;
using InventoryOrderApi.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace InventoryOrderApi.Tests;

public sealed class PostgresReservationTests
{
    [Fact]
    public async Task ConcurrentOrdersCannotReserveTheSameLastUnits()
    {
        var configured = Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(configured)) return; // CI supplies a fresh PostgreSQL service.

        var schema = "it_" + Guid.NewGuid().ToString("N");
        var builder = new NpgsqlConnectionStringBuilder(configured) { SearchPath = schema };
        await using var admin = new NpgsqlConnection(configured);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", admin))
            await create.ExecuteNonQueryAsync();
        try
        {
            var options = new DbContextOptionsBuilder<InventoryDbContext>()
                .UseNpgsql(builder.ConnectionString).Options;
            await using (var setup = new InventoryDbContext(options))
            {
                await setup.Database.EnsureCreatedAsync();
                setup.Categories.Add(new Category { Name = "Parts" });
                await setup.SaveChangesAsync();
                setup.Products.Add(new Product
                {
                    CategoryId = setup.Categories.Single().Id,
                    Sku = "LAST", Name = "Last units", Price = 1, OnHand = 5,
                });
                await setup.SaveChangesAsync();
            }

            async Task<Exception?> Attempt()
            {
                await using var db = new InventoryDbContext(options);
                try { await new OrderService(db).CreateAsync([new(1, 4)]); return null; }
                catch (Exception error) { return error; }
            }

            var outcomes = await Task.WhenAll(Attempt(), Attempt());
            Assert.Single(outcomes, outcome => outcome is null);
            Assert.Single(outcomes, outcome => outcome is DomainException);
            await using var verify = new InventoryDbContext(options);
            Assert.Equal(4, await verify.Products.Select(p => p.Reserved).SingleAsync());
            Assert.Equal(1, await verify.Orders.CountAsync());
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
