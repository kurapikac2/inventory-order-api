using InventoryOrderApi.Data;
using InventoryOrderApi.Domain;
using InventoryOrderApi.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace InventoryOrderApi.Tests;

public sealed class OrderServiceTests : IDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly InventoryDbContext db;
    private readonly OrderService service;

    public OrderServiceTests()
    {
        connection.Open();
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlite(connection).Options;
        db = new InventoryDbContext(options);
        db.Database.EnsureCreated();
        db.Categories.Add(new Category { Id = 1, Name = "Parts" });
        db.Products.AddRange(
            new Product { Id = 1, CategoryId = 1, Sku = "A", Name = "A", Price = 10, OnHand = 5 },
            new Product { Id = 2, CategoryId = 1, Sku = "B", Name = "B", Price = 20, OnHand = 2 });
        db.SaveChanges();
        db.ChangeTracker.Clear();
        service = new OrderService(db);
    }

    [Fact]
    public async Task CreatingOrderReservesAllLinesAndCombinesDuplicates()
    {
        var order = await service.CreateAsync([new(1, 1), new(1, 2), new(2, 1)]);
        Assert.Equal(OrderStatus.Reserved, order.Status);
        Assert.Equal(2, order.Lines.Count);
        Assert.Equal(3, await db.Products.Where(p => p.Id == 1).Select(p => p.Reserved).SingleAsync());
        Assert.Equal(1, await db.Products.Where(p => p.Id == 2).Select(p => p.Reserved).SingleAsync());
    }

    [Fact]
    public async Task InsufficientStockRollsBackAllReservations()
    {
        await Assert.ThrowsAsync<DomainException>(() =>
            service.CreateAsync([new(1, 2), new(2, 3)]));
        Assert.Equal(0, await db.Products.Where(p => p.Id == 1).Select(p => p.Reserved).SingleAsync());
        Assert.Empty(await db.Orders.ToListAsync());
    }

    [Fact]
    public async Task CancelReleasesOnceAndRejectsRepeat()
    {
        var order = await service.CreateAsync([new(1, 2)]);
        await service.CancelAsync(order.Id);
        Assert.Equal(0, await db.Products.Where(p => p.Id == 1).Select(p => p.Reserved).SingleAsync());
        await Assert.ThrowsAsync<DomainException>(() => service.CancelAsync(order.Id));
    }

    [Fact]
    public async Task FulfillDeductsStockOnce()
    {
        var order = await service.CreateAsync([new(1, 2)]);
        await service.FulfillAsync(order.Id);
        var product = await db.Products.AsNoTracking().SingleAsync(p => p.Id == 1);
        Assert.Equal(3, product.OnHand);
        Assert.Equal(0, product.Reserved);
        await Assert.ThrowsAsync<DomainException>(() => service.FulfillAsync(order.Id));
    }

    [Fact]
    public async Task AdjustmentCannotReduceBelowReservation()
    {
        await service.CreateAsync([new(1, 4)]);
        await Assert.ThrowsAsync<DomainException>(() => service.AdjustStockAsync(1, -2));
        Assert.Equal(5, await db.Products.Where(p => p.Id == 1).Select(p => p.OnHand).SingleAsync());
    }

    public void Dispose()
    {
        db.Dispose();
        connection.Dispose();
    }
}
