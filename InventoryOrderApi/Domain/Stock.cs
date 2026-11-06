namespace InventoryOrderApi.Domain;

public sealed class DomainException(string message) : Exception(message);

public sealed class Stock
{
    public int OnHand { get; private set; }
    public int Reserved { get; private set; }
    public int Available => OnHand - Reserved;

    public Stock(int onHand, int reserved = 0)
    {
        if (onHand < 0 || reserved < 0 || reserved > onHand)
            throw new DomainException("Invalid stock quantities.");
        OnHand = onHand;
        Reserved = reserved;
    }

    public void Reserve(int quantity)
    {
        RequirePositive(quantity);
        if (quantity > Available) throw new DomainException("Insufficient available stock.");
        Reserved += quantity;
    }

    public void Release(int quantity)
    {
        RequirePositive(quantity);
        if (quantity > Reserved) throw new DomainException("Cannot release unreserved stock.");
        Reserved -= quantity;
    }

    public void Fulfill(int quantity)
    {
        RequirePositive(quantity);
        if (quantity > Reserved) throw new DomainException("Cannot fulfill unreserved stock.");
        Reserved -= quantity;
        OnHand -= quantity;
    }

    public void Adjust(int delta)
    {
        if ((long)OnHand + delta < Reserved || (long)OnHand + delta > int.MaxValue)
            throw new DomainException("Adjustment would violate reserved stock.");
        OnHand += delta;
    }

    private static void RequirePositive(int quantity)
    {
        if (quantity <= 0) throw new DomainException("Quantity must be positive.");
    }
}
