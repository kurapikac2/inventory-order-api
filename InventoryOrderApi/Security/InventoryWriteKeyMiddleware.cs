using System.Security.Cryptography;
using System.Text;

namespace InventoryOrderApi.Security;

public sealed class InventoryWriteKeyMiddleware(RequestDelegate next, IConfiguration configuration)
{
    private readonly byte[] expectedHash = HashKey(configuration["InventoryWriteKey"]);

    public async Task InvokeAsync(HttpContext context)
    {
        if (HttpMethods.IsPost(context.Request.Method))
        {
            var supplied = context.Request.Headers["X-Inventory-Key"].ToString();
            if (string.IsNullOrEmpty(supplied) ||
                !CryptographicOperations.FixedTimeEquals(expectedHash, SHA256.HashData(Encoding.UTF8.GetBytes(supplied))))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
        }
        await next(context);
    }

    private static byte[] HashKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length < 32)
            throw new InvalidOperationException("InventoryWriteKey must contain at least 32 characters.");
        return SHA256.HashData(Encoding.UTF8.GetBytes(key));
    }
}
