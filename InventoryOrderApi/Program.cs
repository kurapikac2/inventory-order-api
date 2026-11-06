using InventoryOrderApi.Data;
using InventoryOrderApi.Domain;
using InventoryOrderApi.Services;
using InventoryOrderApi.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
var connection = builder.Configuration.GetConnectionString("Inventory")
    ?? throw new InvalidOperationException("ConnectionStrings:Inventory is required.");
builder.Services.AddDbContext<InventoryDbContext>(options => options.UseNpgsql(connection));
builder.Services.AddScoped<OrderService>();
builder.Services.AddOpenApi();

var app = builder.Build();
app.UseMiddleware<InventoryWriteKeyMiddleware>();
if (app.Configuration.GetValue<bool>("ApplyMigrations"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<InventoryDbContext>().Database.MigrateAsync();
}
app.MapOpenApi();

app.MapPost("/categories", async (CreateCategory request, InventoryDbContext db, CancellationToken ct) =>
{
    var name = request.Name?.Trim();
    if (string.IsNullOrWhiteSpace(name) || name.Length > 120) return Results.BadRequest("Name must be 1–120 characters.");
    if (await db.Categories.AnyAsync(c => c.Name == name, ct)) return Results.Conflict("Category name already exists.");
    var category = new Category { Name = name };
    db.Categories.Add(category);
    try { await db.SaveChangesAsync(ct); }
    catch (DbUpdateException error) when (error.InnerException is PostgresException
        { SqlState: PostgresErrorCodes.UniqueViolation })
    { return Results.Conflict("Category name already exists."); }
    return Results.Created($"/categories/{category.Id}", new { category.Id, category.Name });
});

app.MapPost("/products", async (CreateProduct request, InventoryDbContext db, CancellationToken ct) =>
{
    var sku = request.Sku?.Trim();
    var name = request.Name?.Trim();
    if (string.IsNullOrWhiteSpace(sku) || sku.Length > 64 || string.IsNullOrWhiteSpace(name) || name.Length > 160 ||
        !ProductPrice.IsValid(request.Price) || request.OnHand < 0 || request.CategoryId <= 0)
        return Results.BadRequest("Invalid product fields.");
    if (!await db.Categories.AnyAsync(c => c.Id == request.CategoryId, ct)) return Results.BadRequest("Category does not exist.");
    if (await db.Products.AnyAsync(p => p.Sku == sku, ct)) return Results.Conflict("SKU already exists.");
    var product = new Product
    {
        CategoryId = request.CategoryId, Sku = sku, Name = name,
        Price = request.Price, OnHand = request.OnHand,
    };
    db.Products.Add(product);
    try { await db.SaveChangesAsync(ct); }
    catch (DbUpdateException error) when (error.InnerException is PostgresException
        { SqlState: PostgresErrorCodes.UniqueViolation })
    { return Results.Conflict("SKU already exists."); }
    return Results.Created($"/products/{product.Id}", new { product.Id });
});

app.MapGet("/products", async (InventoryDbContext db, int page = 1, int pageSize = 20, int? categoryId = null,
    string? search = null, string? sort = null, CancellationToken ct = default) =>
{
    if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
        return Results.BadRequest("Invalid page or pageSize.");
    if (sort is not (null or "name" or "-name" or "price" or "-price" or "id"))
        return Results.BadRequest("Unsupported sort value.");
    var query = db.Products.AsNoTracking().AsQueryable();
    if (categoryId is not null) query = query.Where(p => p.CategoryId == categoryId);
    if (!string.IsNullOrWhiteSpace(search)) query = query.Where(p => p.Name.Contains(search) || p.Sku.Contains(search));
    query = sort switch
    {
        "name" => query.OrderBy(p => p.Name).ThenBy(p => p.Id),
        "-name" => query.OrderByDescending(p => p.Name).ThenBy(p => p.Id),
        "price" => query.OrderBy(p => p.Price).ThenBy(p => p.Id),
        "-price" => query.OrderByDescending(p => p.Price).ThenBy(p => p.Id),
        _ => query.OrderBy(p => p.Id),
    };
    var total = await query.CountAsync(ct);
    var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
        .Select(p => new { p.Id, p.CategoryId, p.Sku, p.Name, p.Price, p.IsActive, p.OnHand, p.Reserved, Available = p.OnHand - p.Reserved })
        .ToListAsync(ct);
    return Results.Ok(new { page, pageSize, total, items });
});

app.MapPost("/products/{id:int}/stock-adjustments", async (int id, StockAdjustment request,
    OrderService service, CancellationToken ct) =>
{
    try { await service.AdjustStockAsync(id, request.Delta, ct); return Results.NoContent(); }
    catch (DomainException error) { return Results.Conflict(error.Message); }
});

app.MapPost("/orders", async (CreateOrder request, OrderService service, CancellationToken ct) =>
{
    if (request.Items is null || request.Items.Count is < 1 or > 100 ||
        request.Items.Any(i => i.ProductId <= 0 || i.Quantity <= 0))
        return Results.BadRequest("Order needs 1–100 valid lines.");
    try
    {
        var order = await service.CreateAsync(request.Items, ct);
        return Results.Created($"/orders/{order.Id}", new { order.Id, order.Status, order.CreatedAt, order.Lines });
    }
    catch (DomainException error) { return Results.Conflict(error.Message); }
});

app.MapGet("/orders/{id:int}", async (int id, InventoryDbContext db, CancellationToken ct) =>
{
    var order = await db.Orders.AsNoTracking().Include(o => o.Lines).SingleOrDefaultAsync(o => o.Id == id, ct);
    return order is null ? Results.NotFound() : Results.Ok(order);
});

app.MapPost("/orders/{id:int}/cancel", async (int id, OrderService service, CancellationToken ct) =>
{
    try { await service.CancelAsync(id, ct); return Results.NoContent(); }
    catch (DomainException error) { return Results.Conflict(error.Message); }
});

app.MapPost("/orders/{id:int}/fulfill", async (int id, OrderService service, CancellationToken ct) =>
{
    try { await service.FulfillAsync(id, ct); return Results.NoContent(); }
    catch (DomainException error) { return Results.Conflict(error.Message); }
});

app.Run();

public sealed record CreateCategory(string? Name);
public sealed record CreateProduct(int CategoryId, string? Sku, string? Name, decimal Price, int OnHand);
public sealed record StockAdjustment(int Delta);
public sealed record CreateOrder(List<OrderItemInput>? Items);

public partial class Program;
