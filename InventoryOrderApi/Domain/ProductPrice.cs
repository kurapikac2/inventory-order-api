namespace InventoryOrderApi.Domain;

public static class ProductPrice
{
    public static bool IsValid(decimal value) => value >= 0 &&
        value <= 9999999999999999.99m && decimal.Round(value, 2) == value;
}
