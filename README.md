# Products API: Docker, Tests and CI

A small REST API for products, built to practise the production side of backend work: a **multi-stage Docker build**, **automated unit and integration tests**, and a **CI pipeline that blocks failing code from being merged**.

Built for the Elevvo Backend Development internship (Level 3, Task 1).

## What this project demonstrates

- **Multi-stage Dockerfile:** the .NET SDK image builds the app, and only the published output is copied into a small Alpine runtime image that runs as a non-root user. Final image size: **188 MB**.
- **21 automated tests:** unit tests for the controller, service and middleware, plus integration tests that send real HTTP requests through the whole pipeline.
- **CI with a quality gate:** GitHub Actions runs on every pull request to `main`, and a branch ruleset requires the check to pass before merging.
- **Docker Compose (bonus):** the API, PostgreSQL and Redis start together, and the API waits until both services report healthy.

## Tech stack

C# / .NET 10, ASP.NET Core controllers, EF Core with PostgreSQL (Npgsql), xUnit with `WebApplicationFactory`, Docker, Docker Compose, GitHub Actions.

## Quick start

Requires Docker.

```powershell
docker compose up --build
```

Then open `http://localhost:8080/health`.

The compose file also exposes PostgreSQL on host port **5433**, so it doesn't clash with a local PostgreSQL on 5432.

### Run without Docker for the API

```powershell
docker compose up -d postgres
dotnet run --project src/ProductsApi
```

## API

| Method | Path | Result |
|---|---|---|
| GET | `/health` | `{ "status": "OK" }` |
| GET | `/api/products?page=1&pageSize=20` | Paged list (pageSize 1 to 100) |
| GET | `/api/products/{id}` | One product, or 404 |
| POST | `/api/products` | 201 with `Location`, or 400 if invalid |
| PUT | `/api/products/{id}` | Updated product, or 404 |
| DELETE | `/api/products/{id}` | 204, or 404 |

Example request:

```json
POST /api/products
{ "name": "Keyboard", "description": "Mechanical", "price": 49.99, "stock": 10 }
```

## Tests

```powershell
dotnet test
```

| Kind | What it covers |
|---|---|
| Unit | `ProductsController` with a fake service, `ProductService` against an in-memory database, `ExceptionHandlingMiddleware` on its own |
| Integration | Every endpoint over real HTTP: success paths, validation errors (400) and missing resources (404) |

## Docker image

- **Builder stage:** `dotnet/sdk:10.0`. The `.csproj` is copied and restored first, so the dependency layer is cached until packages change.
- **Runner stage:** `dotnet/aspnet:10.0-alpine`, containing only the published output, running as the non-root `app` user.

## CI and merge protection

`.github/workflows/ci.yml` runs on every pull request to `main`:

1. Restore dependencies
2. Run all tests
3. Build in Release
4. Build the Docker image

A branch ruleset on `main` requires the `build-and-test` check to pass, so a pull request with a failing test cannot be merged.

## Project structure

```
src/ProductsApi/          controllers, services, EF Core, middleware
tests/ProductsApi.Tests/  unit and integration tests
.github/workflows/        CI pipeline
Dockerfile                multi-stage build
docker-compose.yml        API + PostgreSQL + Redis with health checks
```

## Design decisions and trade-offs

- **Service layer between controller and database.** Controllers stay thin and are easy to unit test. It also gives a clean place to add caching later.
- **In-memory database for integration tests.** Fast and needs no extra infrastructure in CI. The trade-off is that it doesn't behave exactly like PostgreSQL, so a future improvement is running the tests against a real PostgreSQL container.
- **`EnsureCreated` at startup instead of migrations.** Simple for this task. Real deployments should use EF Core migrations.
- **Redis is already in Compose.** It is not used yet; the next task adds caching and rate limiting on top of it.
- **Passwords in `docker-compose.yml` are for local development only.** Real secrets belong in environment variables or a secret store.


## Author

Anan Hany: [GitHub](https://github.com/ananhany238)
