# Products API: Docker, Tests, CI, Redis Caching and Rate Limiting

A small REST API for products, built to practise the production side of backend work: a **multi-stage Docker build**, **automated tests**, a **CI pipeline that blocks failing code**, **Redis cache-aside caching** and **distributed rate limiting**.

Built for the Elevvo Backend Development internship (Level 3, Tasks 1 and 2).

## What this project demonstrates

- **Multi-stage Dockerfile:** the .NET SDK image builds the app, and only the published output is copied into a small Alpine runtime image that runs as a non-root user. Final image size: **188 MB**.
- **Automated tests:** unit tests for the controller, service, cache decorator and middleware, plus integration tests that send real HTTP requests through the whole pipeline.
- **CI with a quality gate:** GitHub Actions runs on every pull request to `main`, and a branch ruleset requires the check to pass before merging.
- **Cache-aside with Redis:** product reads check Redis first, fall back to PostgreSQL on a miss, and store the result with a **1-hour TTL**.
- **Active cache invalidation:** creating, updating or deleting a product clears what became stale.
- **Distributed rate limiting:** request counters live in Redis, so the limit holds across several API instances.
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

## Tests

```powershell
dotnet test
```

| Kind | What it covers |
|---|---|
| Unit | Controller (fake service), service (in-memory database), exception middleware, cache decorator (hit, miss, TTL, invalidation, cache down), rate limiter (limit, 429, separate clients, fail open) |
| Integration | Every endpoint over real HTTP, cache invalidation end to end, and the 429 response |

Tests use an in-memory database and an in-memory cache, so they need no external services in CI.

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
  Data/ Entities/ Dtos/
tests/ProductsApi.Tests/  unit and integration tests, with in-memory fakes
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
- **In-memory database for integration tests.** Fast, but it doesn't behave exactly like PostgreSQL; running tests against a real PostgreSQL container is a possible improvement.
- **`EnsureCreated` at startup instead of migrations.** Simple for this project; real deployments should use EF Core migrations.
- **Passwords in `docker-compose.yml` are for local development only.**

## Screenshots

![All tests passing](docs/images/tests-passing.png)
![Compose services healthy](docs/images/compose-healthy.png)
![Green CI check](docs/images/ci-green.png)
![Merge blocked by a failing check](docs/images/merge-blocked.png)

## Author

Anan Hany: [GitHub](https://github.com/ananhany238)
