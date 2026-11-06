namespace InventoryOrderApi.Domain;

public sealed class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class Product
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public Category? Category { get; set; }
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public bool IsActive { get; set; } = true;
    public int OnHand { get; set; }
    public int Reserved { get; set; }
}

public enum OrderStatus { Reserved, Cancelled, Fulfilled }

public sealed class Order
{
    public int Id { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Reserved;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<OrderLine> Lines { get; set; } = [];
}

public sealed class OrderLine
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}
