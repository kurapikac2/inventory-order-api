using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using InventoryOrderApi.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace InventoryOrderApi.Tests;

public sealed class ApiWorkflowTests : IDisposable
{
    private const string Key = "12345678901234567890123456789012";
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly WebApplicationFactory<Program> factory;
    private readonly HttpClient client;

    public ApiWorkflowTests()
    {
        connection.Open();
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Inventory", "Host=unused;Database=unused;Username=unused");
            builder.UseSetting("InventoryWriteKey", Key);
            builder.UseSetting("ApplyMigrations", "false");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<InventoryDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<InventoryDbContext>>();
                services.AddDbContext<InventoryDbContext>(options => options.UseSqlite(connection));
            });
        });
        client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<InventoryDbContext>().Database.EnsureCreated();
    }

    [Fact]
    public async Task WriteKeyProtectsCompleteOrderWorkflow()
    {
        var denied = await client.PostAsJsonAsync("/categories", new { name = "Parts" });
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        client.DefaultRequestHeaders.Add("X-Inventory-Key", Key);

        var category = await client.PostAsJsonAsync("/categories", new { name = "Parts" });
        Assert.Equal(HttpStatusCode.Created, category.StatusCode);
        var product = await client.PostAsJsonAsync("/products", new
        {
            categoryId = 1, sku = "A", name = "Part A", price = 10.25m, onHand = 5,
        });
        Assert.Equal(HttpStatusCode.Created, product.StatusCode);
        var order = await client.PostAsJsonAsync("/orders", new
        {
            items = new[] { new { productId = 1, quantity = 3 } },
        });
        Assert.Equal(HttpStatusCode.Created, order.StatusCode);
        using var body = JsonDocument.Parse(await order.Content.ReadAsStringAsync());
        var id = body.RootElement.GetProperty("id").GetInt32();

        var listing = await client.GetFromJsonAsync<JsonElement>("/products?page=1&pageSize=10&sort=name");
        Assert.Equal(2, listing.GetProperty("items")[0].GetProperty("available").GetInt32());
        var fulfill = await client.PostAsync($"/orders/{id}/fulfill", null);
        Assert.Equal(HttpStatusCode.NoContent, fulfill.StatusCode);
        var repeated = await client.PostAsync($"/orders/{id}/fulfill", null);
        Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);
        var after = await client.GetFromJsonAsync<JsonElement>("/products?page=1&pageSize=10");
        Assert.Equal(2, after.GetProperty("items")[0].GetProperty("onHand").GetInt32());
        Assert.Equal(0, after.GetProperty("items")[0].GetProperty("reserved").GetInt32());
    }

    public void Dispose()
    {
        client.Dispose();
        factory.Dispose();
        connection.Dispose();
    }
}
