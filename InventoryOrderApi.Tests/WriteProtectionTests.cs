using InventoryOrderApi.Domain;
using InventoryOrderApi.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace InventoryOrderApi.Tests;

public sealed class WriteProtectionTests
{
    private const string Key = "12345678901234567890123456789012";

    [Fact]
    public async Task PostRequiresConfiguredWriteKey()
    {
        var middleware = CreateMiddleware();
        var denied = new DefaultHttpContext();
        denied.Request.Method = "POST";
        await middleware.InvokeAsync(denied);
        Assert.Equal(401, denied.Response.StatusCode);

        var allowed = new DefaultHttpContext();
        allowed.Request.Method = "POST";
        allowed.Request.Headers["X-Inventory-Key"] = Key;
        await middleware.InvokeAsync(allowed);
        Assert.Equal(204, allowed.Response.StatusCode);
    }

    [Fact]
    public async Task GetRemainsPublic()
    {
        var middleware = CreateMiddleware();
        var request = new DefaultHttpContext();
        request.Request.Method = "GET";
        await middleware.InvokeAsync(request);
        Assert.Equal(204, request.Response.StatusCode);
    }

    [Theory]
    [InlineData("0.01", true)]
    [InlineData("9999999999999999.99", true)]
    [InlineData("-0.01", false)]
    [InlineData("1.001", false)]
    [InlineData("10000000000000000", false)]
    public void PriceMustFitDatabaseColumnWithoutRounding(string value, bool expected)
    {
        Assert.Equal(expected, ProductPrice.IsValid(decimal.Parse(value,
            System.Globalization.CultureInfo.InvariantCulture)));
    }

    private static InventoryWriteKeyMiddleware CreateMiddleware()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["InventoryWriteKey"] = Key,
        }).Build();
        return new InventoryWriteKeyMiddleware(context =>
        {
            context.Response.StatusCode = 204;
            return Task.CompletedTask;
        }, config);
    }
}
