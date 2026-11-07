# Reproducible local demo (Windows PowerShell)

This walkthrough demonstrates a stock reservation conflict and the order lifecycle. It uses **disposable example data** and assumes the service is running locally.

## 1. Start PostgreSQL and the API

Install Docker Desktop with Docker Compose, then in the repository root:

```powershell
Copy-Item .env.example .env
```

Edit `.env` and replace example values with a unique **local** `POSTGRES_PASSWORD` and `INVENTORY_WRITE_KEY` of at least 32 characters. Do not commit this file.

```powershell
docker compose up --build -d
```

When startup completes, open `http://127.0.0.1:8080/openapi/v1.json`. The API binds to loopback only.

## 2. Create sample data and reserve stock

In the same PowerShell session:

```powershell
$base = 'http://127.0.0.1:8080'
$key = Read-Host 'Enter the local INVENTORY_WRITE_KEY from .env'
$headers = @{ 'X-Inventory-Key' = $key }
$suffix = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()

$category = Invoke-RestMethod "$base/categories" -Method Post -Headers $headers -ContentType 'application/json' -Body (@{ name = "Demo-$suffix" } | ConvertTo-Json)
$product = Invoke-RestMethod "$base/products" -Method Post -Headers $headers -ContentType 'application/json' -Body (@{
  categoryId = $category.id
  sku = "DEMO-$suffix"
  name = 'Demo laptop'
  price = 500
  onHand = 5
} | ConvertTo-Json)

$bodyFour = @{ items = @(@{ productId = $product.id; quantity = 4 }) } | ConvertTo-Json -Depth 5
$order = Invoke-RestMethod "$base/orders" -Method Post -Headers $headers -ContentType 'application/json' -Body $bodyFour
Invoke-RestMethod "$base/products"
```

At this point the demo product should show `OnHand=5`, `Reserved=4`, `Available=1`.

## 3. Verify insufficient stock is rejected

```powershell
try {
  $null = Invoke-RestMethod "$base/orders" -Method Post -Headers $headers -ContentType 'application/json' -Body $bodyFour -ErrorAction Stop
  throw 'Unexpected success: the order should have been rejected.'
} catch {
  $status = [int]$_.Exception.Response.StatusCode
  if ($status -ne 409) { throw }
  Write-Host 'Expected conflict: HTTP 409'
}
```

A second order for four units must fail. The first reservation should remain intact.

## 4. Cancel and fulfill

```powershell
Invoke-RestMethod "$base/orders/$($order.id)/cancel" -Method Post -Headers $headers
Invoke-RestMethod "$base/products"

$bodyTwo = @{ items = @(@{ productId = $product.id; quantity = 2 }) } | ConvertTo-Json -Depth 5
$secondOrder = Invoke-RestMethod "$base/orders" -Method Post -Headers $headers -ContentType 'application/json' -Body $bodyTwo
Invoke-RestMethod "$base/orders/$($secondOrder.id)/fulfill" -Method Post -Headers $headers
Invoke-RestMethod "$base/products"
```

For the demo product, the expected final state is **OnHand=3, Reserved=0, Available=3**.

Do not treat this document as a recorded local test result. Run it against the current build and verify the response values.

## 5. Automated checks

```powershell
dotnet restore
dotnet test --configuration Release
dotnet build --configuration Release
```

GitHub Actions also exercises PostgreSQL concurrency and Docker startup. See the workflow results for the commit being reviewed.

## 6. Stop safely

```powershell
docker compose down
```

This retains the Docker volume. **Do not use `docker compose down --volumes` unless you intentionally want to erase the local demo database.**

## Interview questions

- What problem does conditional `ExecuteUpdateAsync` solve?
- Why must multi-line orders use one transaction?
- What changes when an order is cancelled versus fulfilled?
- Why is a shared API key insufficient for a public multi-user system?
- How do the tests differ between SQLite and PostgreSQL?
