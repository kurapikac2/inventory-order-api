using InventoryOrderApi.Data;
using InventoryOrderApi.Domain;
using Microsoft.EntityFrameworkCore;

namespace InventoryOrderApi.Services;

public sealed record OrderItemInput(int ProductId, int Quantity);

public sealed class OrderService(InventoryDbContext db)
{
    public async Task<Order> CreateAsync(IEnumerable<OrderItemInput> items, CancellationToken ct = default)
    {
        var input = items.ToList();
        if (input.Count == 0 || input.Any(i => i.ProductId <= 0 || i.Quantity <= 0))
            throw new DomainException("An order requires products with positive quantities.");

        var lines = input.GroupBy(i => i.ProductId)
            .Select(g => new OrderItemInput(g.Key, checked(g.Sum(i => (long)i.Quantity)) > int.MaxValue
                ? throw new DomainException("Quantity is too large.") : (int)g.Sum(i => (long)i.Quantity)))
            .OrderBy(i => i.ProductId).ToList();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var order = new Order();
        foreach (var item in lines)
        {
            var product = await db.Products.AsNoTracking()
                .SingleOrDefaultAsync(p => p.Id == item.ProductId && p.IsActive, ct)
                ?? throw new DomainException("Product is unavailable.");
            var changed = await db.Products.Where(p => p.Id == item.ProductId && p.IsActive &&
                    p.OnHand - p.Reserved >= item.Quantity)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Reserved, p => p.Reserved + item.Quantity), ct);
            if (changed != 1) throw new DomainException("Insufficient available stock.");
            order.Lines.Add(new OrderLine
            {
                ProductId = item.ProductId, Quantity = item.Quantity, UnitPrice = product.Price,
            });
        }
        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        db.ChangeTracker.Clear();
        return order;
    }

    public Task CancelAsync(int id, CancellationToken ct = default) => TransitionAsync(id, OrderStatus.Cancelled, ct);
    public Task FulfillAsync(int id, CancellationToken ct = default) => TransitionAsync(id, OrderStatus.Fulfilled, ct);

    private async Task TransitionAsync(int id, OrderStatus target, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var changed = await db.Orders.Where(o => o.Id == id && o.Status == OrderStatus.Reserved)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, target), ct);
        if (changed != 1) throw new DomainException("Order is missing or already closed.");
        var lines = await db.OrderLines.AsNoTracking().Where(l => l.OrderId == id).ToListAsync(ct);
        foreach (var line in lines.OrderBy(l => l.ProductId))
        {
            var query = db.Products.Where(p => p.Id == line.ProductId && p.Reserved >= line.Quantity);
            var updated = target == OrderStatus.Cancelled
                ? await query.ExecuteUpdateAsync(s => s.SetProperty(p => p.Reserved, p => p.Reserved - line.Quantity), ct)
                : await query.ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.Reserved, p => p.Reserved - line.Quantity)
                    .SetProperty(p => p.OnHand, p => p.OnHand - line.Quantity), ct);
            if (updated != 1) throw new DomainException("Inventory conflict. Please retry.");
        }
        await transaction.CommitAsync(ct);
        db.ChangeTracker.Clear();
    }

    public async Task AdjustStockAsync(int productId, int delta, CancellationToken ct = default)
    {
        if (delta == 0) throw new DomainException("Adjustment must be nonzero.");
        var changed = await db.Products.Where(p => p.Id == productId &&
                (long)p.OnHand + delta >= p.Reserved && (long)p.OnHand + delta <= int.MaxValue)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.OnHand, p => p.OnHand + delta), ct);
        if (changed != 1) throw new DomainException("Product is missing or adjustment conflicts with reservations.");
        db.ChangeTracker.Clear();
    }
}
