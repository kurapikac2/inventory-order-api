# Verification scope

Reviewed 2026-11-09 on Windows, .NET SDK 10.0.402 and an isolated PostgreSQL 17.11 instance.

- Before the change, running without PostgreSQL misleadingly reported 18 passing tests: the concurrency method returned early.
- After the change, no connection explicitly reports **17 passed / 1 skipped** with a setup reason.
- With `TEST_POSTGRES_CONNECTION` pointing to a fresh dedicated PostgreSQL database: **18 passed / 0 failed / 0 skipped**. The database test creates and removes only its generated test schema; no pre-existing application data is used.
- NuGet advisory checking (`dotnet list package --vulnerable --include-transitive`) reported no vulnerable packages from the configured NuGet source.
- Gitleaks full-history scan reported two occurrences of the same deliberately public, disposable CI-only write-key fixture in workflow commits. Source review confirmed it is passed only to that run's local test containers; it is not a credential for a deployed service. No live credential was identified. These findings were reviewed rather than silently suppressed.

The new `scripts/demo.ps1` verifies initial stock, reservation, insufficient-stock conflict, cancellation and fulfillment using HTTP and rereads persisted state after each step. GitHub Actions runs it against the actual PostgreSQL-backed Compose app and uploads `demo.json` plus TRX reports as `inventory-verification`. This extends the existing SQLite HTTP workflow test with a real network/PostgreSQL demo. Use the PR and exact CI run for execution evidence; local Docker was not run.

This is a local portfolio API. The shared write key does not establish user identity, read authorization, scoped roles, key rotation or an audit trail. No deployment or license approval is implied by green tests.
