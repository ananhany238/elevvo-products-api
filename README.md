# Products API: Docker, CI, Redis, Webhooks, Real-Time Notifications and Backup Recovery

A small REST API for products, built to practise the production side of backend work: a **multi-stage Docker build**, **automated tests**, a **CI pipeline that blocks failing code**, **Redis caching and distributed rate limiting**, a **signed, idempotent webhook receiver**, **real-time notifications over private rooms**, and a **tested PostgreSQL backup and recovery process**.

Built for the Elevvo Backend Development internship (Level 3, Tasks 1 to 5).

## What this project demonstrates

- **Multi-stage Dockerfile:** the .NET SDK image builds the app, and only the published output is copied into a small Alpine runtime image that runs as a non-root user. Final image size: **188 MB**.
- **Automated tests:** unit tests for the controller, service, cache decorator and middleware, plus integration tests that send real HTTP requests through the whole pipeline.
- **CI with a quality gate:** GitHub Actions runs on every pull request to `main`, and a branch ruleset requires the check to pass before merging.
- **Cache-aside with Redis:** product reads check Redis first, fall back to PostgreSQL on a miss, and store the result with a **1-hour TTL**.
- **Active cache invalidation:** creating, updating or deleting a product clears what became stale.
- **Distributed rate limiting:** request counters live in Redis, so the limit holds across several API instances.
- **Secure webhooks:** HMAC-SHA256 signatures over the raw body bytes, constant-time comparison, and replay protection with a timestamp window.
- **Idempotent processing:** a UNIQUE index on the event id plus one database transaction, so retried or concurrent deliveries can never apply the same event twice.
- **Transactional outbox (bonus):** a background worker delivers follow-up calls with retries, without duplicating events.
- **Real-time notifications:** a SignalR hub that pushes order-status events to a user's private room, with JWT authentication.
- **Identity-derived rooms:** the room comes from the verified token, never from anything the client sends, and a reconnect derives it again.
- **Acknowledgements and bounded retries (bonus):** unacknowledged events are re-sent a limited number of times, and event ids stop duplicates reaching the screen.
- **Backup and recovery (Task 5):** `pg_dump -Fc` backups, `pg_restore` into a separate database, automated validation, measured RTO, estimated RPO, retention, and a recovery runbook.
- **Docker Compose:** the API, PostgreSQL and Redis start together, and the API waits until both services are healthy.

## Tech stack

C# / .NET 10, ASP.NET Core controllers, EF Core with PostgreSQL (Npgsql), Redis (StackExchange.Redis), xUnit with `WebApplicationFactory`, Docker, Docker Compose, GitHub Actions.

## Quick start

Requires Docker.

```powershell
docker compose up --build
```

Then open `http://localhost:8080/health`.

PostgreSQL is exposed on host port **5433** (so it doesn't clash with a local PostgreSQL on 5432) and Redis on **6379**.

### Run the API without Docker

```powershell
docker compose up -d postgres redis
dotnet run --project src/ProductsApi
```

## API

| Method | Path | Result |
|---|---|---|
| GET | `/health` | `{ "status": "OK" }` (never rate limited) |
| GET | `/api/products?page=1&pageSize=20` | Paged list (pageSize 1 to 100), cached |
| GET | `/api/products/{id}` | One product, or 404, cached |
| POST | `/api/products` | 201 with `Location`, or 400 if invalid |
| PUT | `/api/products/{id}` | Updated product, or 404 |
| DELETE | `/api/products/{id}` | 204, or 404 |

## Caching (cache-aside)

`CachedProductService` wraps the real `ProductService`:

1. **Read:** look in Redis. On a **hit**, return it. On a **miss**, read PostgreSQL, store the JSON in Redis with a **1-hour TTL**, and return it.
2. **Write (`POST`, `PUT`, `DELETE`):** do the write, then invalidate.
   - A single product key (`products:item:{id}`) is deleted directly.
   - List pages are cached under a key that contains a **version number** (`products:list:v{n}:page{p}:size{s}`). Any write increments the version, so every old list key becomes unreachable at once and simply expires.
3. **If Redis is down,** the API keeps working: cache errors are logged and the request is served from the database.

Look inside the cache while the API runs:

```powershell
docker compose exec redis redis-cli keys "*"
docker compose exec redis redis-cli ttl "products:list:v0:page1:size20"
```

## Rate limiting

A fixed-window limiter (`RateLimitingMiddleware`) counts requests per client IP in Redis (default: **100 requests per 60 seconds**, configurable under `RateLimit` in `appsettings.json`).

- The counter is incremented with a small Lua script, so the increment and the expiry happen **atomically** in Redis.
- Responses include `X-RateLimit-Limit` and `X-RateLimit-Remaining`. Over the limit, the API returns **429** with a `Retry-After` header.
- Because the counter is in Redis and not in each API's memory, running several instances does not multiply the limit.
- If Redis is down, the limiter **fails open** (requests are allowed) instead of taking the API down.

## Webhook receiver (Task 3)

`POST /api/webhooks/events` receives simulated stock events:

```json
{ "id": "evt_123", "type": "stock.adjusted", "data": { "productId": 1, "delta": 5 } }
```

The side effect is a change to the product's stock, so applying an event twice would be a real bug.

### Signing scheme

| Item | Value |
|---|---|
| Headers | `X-Webhook-Timestamp` (unix seconds) and `X-Webhook-Signature` (`sha256=<hex>`) |
| Signed bytes | ASCII(timestamp) + `"."` + the **exact raw request-body bytes** |
| Algorithm | HMAC-SHA256 with the shared secret |
| Comparison | Constant time (`CryptographicOperations.FixedTimeEquals`), after checking the signature has the right format and length |
| Tolerance | 300 seconds (configurable), in both directions |

The body is read as raw bytes and verified **before** any JSON parsing, because re-serialising JSON can change the bytes. The signing secret comes from the `Webhook__Secret` environment variable and is never committed or logged. If it is missing, the endpoint answers 503 (it fails closed).

### Status codes

| Code | `status` / `error` | Meaning |
|---|---|---|
| 201 | `processed` | New event, applied |
| 200 | `duplicate` | Verified duplicate: already processed, nothing changed |
| 400 | `malformed_json`, `invalid_schema` | Authenticated, but the body is not valid |
| 401 | `invalid_signature`, `invalid_timestamp` | Missing, malformed or wrong signature or timestamp |
| 401 | `stale_timestamp` | Correctly signed, but outside the tolerance window |
| 422 | `unprocessable_event` | Product not found, or stock would go below zero (nothing is recorded) |
| 503 | `not_configured` | No signing secret configured |

Logs contain event ids and outcomes only: never the secret, the signature or the payload.

### Idempotency in short

The event id is inserted into a table with a UNIQUE index, in the same transaction as the stock change and the outbox row. The first delivery wins, every other delivery of that id gets 200 `duplicate`. The full explanation is in [docs/webhook-idempotency.md](docs/webhook-idempotency.md), and the tables are in [db/schema/webhooks.sql](db/schema/webhooks.sql).

### Outbox (bonus)

The same transaction also writes an outbox row. `OutboxWorker` delivers pending rows to a simulated external system and retries failures on the same row with exponential backoff (up to 5 attempts). The event id is sent as an idempotency key, because delivery is at least once.

### Try it

```powershell
# 1. Set a secret (use a long random value) and start the API
$env:Webhook__Secret = "change-me-to-a-long-random-value"
docker compose up -d postgres redis
dotnet run --project src/ProductsApi

# 2. In another window (same secret), send signed events
$env:Webhook__Secret = "change-me-to-a-long-random-value"
.\scripts\send-webhook.ps1 -ProductId 1 -Delta 5                      # 201 processed
.\scripts\send-webhook.ps1 -EventId evt_demo -ProductId 1 -Delta 5    # 201, then run again: 200 duplicate
.\scripts\send-webhook.ps1 -TimestampOffsetSeconds -3600              # 401 stale_timestamp
.\scripts\send-webhook.ps1 -Tamper                                    # 401 invalid_signature
```

To watch the outbox retry, start the API with `$env:Outbox__SimulatedFailuresPerEvent = "2"` and send an event: the log shows two failed attempts and then a delivery.

For `docker compose up --build`, put `WEBHOOK_SECRET=...` in a local `.env` file (it is git-ignored).

## Real-time notifications (Task 4)

Built with ASP.NET Core **SignalR**, the .NET equivalent of Socket.IO (connections, events and groups, which are SignalR's "rooms").

| Piece | What it does |
|---|---|
| `POST /api/auth/login` | Checks the two local test users and returns a short-lived signed JWT |
| `/hubs/notifications` | SignalR hub, `[Authorize]`: no valid token, no connection |
| Room `user:{userId}` | The connection is added to it by the **server**, using the user id inside the verified token |
| `POST /api/dev/simulate-status` | Stands in for the order system: publishes `{ eventId, orderId, status, timestamp }` to one user's room. Needs the server's `X-Api-Key`; disabled (404) if no key is set |
| `Acknowledge(eventId)` | The only method a client can call. It only affects that user's own pending events |

Full explanation of identity and room access: [docs/realtime-identity-and-rooms.md](docs/realtime-identity-and-rooms.md).

### Event shape

```json
{ "eventId": "evt_0f8e...", "orderId": "ord_1001", "status": "shipped", "timestamp": "2026-11-24T12:00:00Z" }
```

### Reliability (bonus)

- The server keeps each event as pending until the client acknowledges it.
- Unacknowledged events are re-sent after 2s, 4s, 8s ... (capped at 30s), at most 5 attempts in total, then dropped with a log line.
- When a user reconnects, their still-pending events are sent again to that user only.
- Clients keep the event ids they have shown and ignore repeats, so a repeated delivery never shows twice.

### Try it live

```powershell
# Window 1: environment + API
. .\scripts\dev-env.ps1
dotnet run --project src/ProductsApi

# Window 2: Alice's client        Window 3: Bob's client
. .\scripts\dev-env.ps1               . .\scripts\dev-env.ps1
dotnet run --project tools/NotificationClient -- alice
                                       dotnet run --project tools/NotificationClient -- bob

# Window 4: simulate the order system
. .\scripts\dev-env.ps1
.\scripts\simulate-status.ps1 -User alice -Order ord_1001 -Status shipped   # only Alice's window prints it
.\scripts\simulate-status.ps1 -User bob   -Order ord_2002 -Status delivered # only Bob's window prints it
```

The secrets are generated once into `.dev-env.json` (git-ignored). For `docker compose up --build`, put `JWT_KEY`, `DEMO_ALICE_PASSWORD`, `DEMO_BOB_PASSWORD` and `SIMULATION_KEY` in a local `.env` file.

## Backup and recovery (Task 5)

PowerShell scripts in `scripts/backup/` (settings come from the `PGUSER`, `PGPASSWORD` and `PGDATABASE` environment variables; credentials and backups are never committed):

| Script | What it does |
|---|---|
| `Backup-Database.ps1` | `pg_dump -Fc` backup, checks the exit code, the file size and `pg_restore --list`, writes a manifest (row counts, content checksums, sample records), logs UTC timestamps, applies the retention policy (newest N kept) |
| `Restore-Database.ps1` | `pg_restore` into a **separate** `<db>_restore_test` database, then validates it against the manifest and runs smoke queries |
| `Invoke-RecoveryDrill.ps1` | Full drill on disposable sample data: backup, writes after the backup, simulated data loss, restore, validation, negative control, **RTO and RPO**, Markdown report |
| `Test-RestoreInContainer.ps1` | Bonus: restores into a brand-new disposable container and **fails the script** if validation does not pass |

Safety rules built into the scripts: only databases named `*_restore_test` or `*_drill` can ever be dropped or overwritten, and every step checks exit codes and stops with a non-zero exit code on failure.

```powershell
. .\scripts\backup\env.example.ps1               # dev values for the throw-away database
.\scripts\backup\Backup-Database.ps1 -Keep 5
.\scripts\backup\Restore-Database.ps1
.\scripts\backup\Invoke-RecoveryDrill.ps1 -AlsoInContainer
```

The recovery procedure, how RTO and RPO are measured, and the limits compared with production disaster recovery are in [docs/recovery-runbook.md](docs/recovery-runbook.md). Drill output is in `docs/drill-output/`.

## Tests

```powershell
dotnet test
```

| Kind | What it covers |
|---|---|
| Unit | Controller (fake service), service (in-memory database), exception middleware, cache decorator (hit, miss, TTL, invalidation, cache down), rate limiter (limit, 429, separate clients, fail open) |
| Integration | Every endpoint over real HTTP, cache invalidation end to end, and the 429 response |
| Webhook unit | Signature scheme (tampering, wrong secret, malformed headers, tolerance edges), schema validation, outbox retries and give-up |
| Real-time unit | Room derivation from claims (suspicious ids rejected), pending-event tracker |
| Real-time integration | Each user receives only their own events, forged or missing or expired tokens are refused, client-supplied ids and non-existent join methods are useless, disconnect and reconnect re-derive the room, acknowledgements, bounded retries, duplicate protection, simulation endpoint security |
| Webhook integration | Valid, invalid-signature, stale, malformed, repeated and **concurrent** deliveries, over real HTTP and real SQL transactions |

Tests use a throw-away SQLite database (a real relational engine, so unique indexes and transactions behave like production) and an in-memory cache, so they need no external services in CI.

## Docker image

- **Builder stage:** `dotnet/sdk:10.0`. The `.csproj` is restored first, so the dependency layer is cached until packages change.
- **Runner stage:** `dotnet/aspnet:10.0-alpine`, containing only the published output, running as the non-root `app` user.

## CI and merge protection

`.github/workflows/ci.yml` runs on every pull request to `main`: restore, test, build in Release, and build the Docker image. A branch ruleset requires the `build-and-test` check, so a pull request with a failing test cannot be merged.

## Project structure

```
src/ProductsApi/
  Caching/        ICacheStore and the Redis implementation
  Controllers/    HTTP endpoints
  Services/       ProductService and the caching decorator
  Middleware/     exception handling and rate limiting
  Webhooks/       signature verification, validation, idempotent processor
  Outbox/         outbox processor, background worker, simulated external system
  Auth/           JWT issuing and validation, the two demo users
  Realtime/       SignalR hub, rooms, notification service, retry worker
  Data/ Entities/ Dtos/
tests/ProductsApi.Tests/  unit and integration tests, with in-memory fakes
scripts/                  dev-env.ps1, send-webhook.ps1, simulate-status.ps1
scripts/backup/           backup, restore, recovery drill and container restore test
backups/                  backup files (git-ignored, never committed)
tools/NotificationClient/ small live client for the real-time demo
docs/                     idempotency, real-time identity, recovery runbook, drill output
db/schema/                reference SQL for the webhook tables
.github/workflows/        CI pipeline
Dockerfile                multi-stage build
docker-compose.yml        API + PostgreSQL + Redis with health checks
```

## Design decisions and trade-offs

- **Decorator for caching.** Controllers and the real service know nothing about Redis. Caching can be added, tested or removed on its own.
- **`ICacheStore` abstraction.** Tests use an in-memory fake, so CI needs no Redis.
- **Version number for list invalidation.** Lists are cached per page and per page size, and Redis can't easily "delete all list keys" without scanning. Bumping one counter invalidates them all in one cheap step; the old entries expire by TTL.
- **Cache failures never break requests.** A cache is an optimisation, not a dependency. The trade-off: if Redis is down during a write, stale entries may remain until their TTL (1 hour).
- **Missing products are not cached.** A product created later is found immediately.
- **Fixed-window rate limiting.** Simple and cheap. Its known weakness is bursts at window edges; a sliding window or token bucket would smooth that.
- **Client identity is the remote IP.** Behind a reverse proxy, the real client IP should come from forwarded headers.
- **The database enforces idempotency, not application code.** A "check then insert" in code has a race between the check and the insert; a UNIQUE index inside a transaction does not.
- **Authenticate before parsing.** Nothing in the body is trusted until the signature is verified over the raw bytes.
- **422 does not record the event.** If an event cannot be applied, the transaction is rolled back, so the sender can fix the cause and retry the same id.
- **Webhook changes invalidate the cache.** The webhook bypasses the caching decorator, so it clears the affected keys itself after the commit.
- **No "join room" method at all.** Safer than validating a requested room: there is nothing for a client to forge.
- **Rooms are per connection.** A reconnect is a new connection, so authorization is re-checked instead of inherited.
- **At-least-once delivery plus event ids.** Acknowledgements and retries can deliver an event twice; the client ignores ids it has shown.
- **In-memory pending events.** Fine for one instance; scaling out needs a SignalR backplane and a shared store.
- **SQLite for integration tests.** It is real SQL with real constraints and transactions, but not identical to PostgreSQL; running tests against a PostgreSQL container is a possible improvement.
- **`EnsureCreated` at startup instead of migrations.** Simple for this project, but it does nothing on a database that already has tables, so after changing the model the dev database must be reset. Real deployments should use EF Core migrations.
- **Passwords in `docker-compose.yml` are for local development only.**

## Screenshots

![All tests passing](docs/images/tests-passing.png)
![Compose services healthy](docs/images/compose-healthy.png)
![Green CI check](docs/images/ci-green.png)
![Merge blocked by a failing check](docs/images/merge-blocked.png)

## Author

Anan Hany: [GitHub](https://github.com/ananhany238)
