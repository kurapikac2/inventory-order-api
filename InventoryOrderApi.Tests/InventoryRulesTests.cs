using InventoryOrderApi.Domain;

namespace InventoryOrderApi.Tests;

public class InventoryRulesTests
{
    [Fact]
    public void ReserveRejectsOverselling()
    {
        var stock = new Stock(5, 3);
        Assert.Throws<DomainException>(() => stock.Reserve(3));
        Assert.Equal(3, stock.Reserved);
    }

    [Fact]
    public void ReserveAndFulfillMaintainAvailability()
    {
        var stock = new Stock(5, 1);
        stock.Reserve(2);
        Assert.Equal(2, stock.Available);
        stock.Fulfill(2);
        Assert.Equal(3, stock.OnHand);
        Assert.Equal(1, stock.Reserved);
        Assert.Equal(2, stock.Available);
    }

    [Fact]
    public void AdjustmentCannotCrossReservations()
    {
        var stock = new Stock(5, 4);
        Assert.Throws<DomainException>(() => stock.Adjust(-2));
        Assert.Equal(5, stock.OnHand);
    }

    [Fact]
    public void ReleaseCannotExceedReserved()
    {
        var stock = new Stock(5, 1);
        Assert.Throws<DomainException>(() => stock.Release(2));
    }
}
