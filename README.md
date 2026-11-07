# Inventory Order API

A small ASP.NET Core API for stock reservations and order lifecycle rules. It uses PostgreSQL and Entity Framework Core. The focus is on preventing overselling and keeping multi-item orders consistent, rather than exposing a generic CRUD layer.


**Walkthrough:** [Run a sample stock reservation and cancellation](docs/DEMO.md) · [Architecture and concurrency decisions](docs/ARCHITECTURE.md)

## Business rules

- `available = onHand - reserved`; stock cannot fall below existing reservations.
- An order combines duplicate product lines, reserves every item in one database transaction, and rolls back if any item is unavailable.
- A reserved order can be cancelled or fulfilled once. Cancellation releases stock. Fulfillment decreases both on-hand and reserved quantities.
- Conditional database updates check current availability, so two requests cannot both reserve the same last units. The affected-row count determines whether the operation succeeded.
- Each order line stores its unit price at creation; later catalog changes do not change the order's price snapshot.

## Stack and structure

.NET 10, ASP.NET Core minimal API, EF Core, PostgreSQL, xUnit, SQLite (local integration tests), Docker Compose, and GitHub Actions.

- `InventoryOrderApi/Domain`: entities and `Stock` invariant checks.
- `InventoryOrderApi/Data`: EF Core context and initial migration.
- `InventoryOrderApi/Services/OrderService.cs`: transactional reservation and state transitions.
- `InventoryOrderApi/Program.cs`: HTTP endpoints and request validation.
- `InventoryOrderApi.Tests`: unit tests for stock rules and database integration tests for order transitions and rollback.

## Run with Docker Compose

Copy `.env.example` to `.env` and replace both example values with a local database password and a random write key of at least 32 characters. Then run:

```bash
docker compose up --build
```

The API listens on `http://localhost:8080`. Compose enables `ApplyMigrations=true`, which applies the included EF migration at startup after PostgreSQL becomes healthy. Use a separate reviewed migration step for a deployed environment.

OpenAPI JSON is at `http://localhost:8080/openapi/v1.json`.

## Run without Docker

Install the .NET 10 SDK and PostgreSQL. Set an environment variable with a database-specific connection string, for example:

```powershell
$env:ConnectionStrings__Inventory = 'Host=localhost;Database=inventory;Username=inventory;Password=<your-local-password>'
$env:InventoryWriteKey = '<a-random-key-of-at-least-32-characters>'
$env:ApplyMigrations = 'true'
dotnet run --project InventoryOrderApi
```

The migration can instead be applied with `dotnet ef database update --project InventoryOrderApi --startup-project InventoryOrderApi` if the EF CLI tool is installed.

## Endpoints

| Method | Path | Purpose |
| --- | --- | --- |
| POST | `/categories` | Add a category |
| POST | `/products` | Add a product with initial stock |
| GET | `/products` | List with `page`, `pageSize`, `categoryId`, `search`, and `sort` |
| POST | `/products/{id}/stock-adjustments` | Change on-hand stock by `delta` |
| POST | `/orders` | Reserve one or more products atomically |
| GET | `/orders/{id}` | Read order and price snapshots |
| POST | `/orders/{id}/cancel` | Release reservation once |
| POST | `/orders/{id}/fulfill` | Consume reservation once |

For example, after creating a category and product:

```json
POST /orders
{"items":[{"productId":1,"quantity":2}]}
```

Invalid request fields return `400`. Inventory and order-state conflicts return `409`. Successful writes return `201` or `204`.
Every `POST` requires `X-Inventory-Key` with the configured write key; otherwise the API returns `401`. Compose binds the API to `127.0.0.1` only. Use TLS, an identity provider, and scoped roles before exposing the service to untrusted clients.

## Checks

```bash
dotnet restore
dotnet test --configuration Release
dotnet build --configuration Release
```

CI runs these commands with a PostgreSQL service. Unit tests cover `Stock`; SQLite-backed integration tests cover order creation, duplicate lines, rollback, cancellation, fulfillment, and adjustments. An in-process HTTP test checks write-key enforcement and an order workflow. A PostgreSQL test uses a temporary schema to check competing reservations. Locally, that test runs only when `TEST_POSTGRES_CONNECTION` is set. CI also smoke-tests Docker Compose startup, OpenAPI, and unauthenticated write rejection. The API has not been deployed.

## Current limitations

- A single write key protects mutations in this demo, but it does not provide user identities, roles, rotation, or an audit trail. Add those before public deployment.
- It has no stock movement audit log, idempotency keys, or customer accounts.
- Search uses simple text matching. Pagination is offset-based.
- The local test environment has no Docker; Compose is checked in CI but has not been exercised on this Windows machine.
