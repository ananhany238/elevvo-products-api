# Products API (Elevvo Backend Track, Level 3, Task 1)

A small ASP.NET Core 10 REST API for products, used to practise **multi-stage Docker builds**, **automated tests** and a **CI pipeline** with a quality gate.

## Tech
C# / .NET 10, ASP.NET Core controllers, EF Core + PostgreSQL, xUnit + `WebApplicationFactory`, Docker, Docker Compose, GitHub Actions.

## Endpoints
| Method | Path | Result |
|---|---|---|
| GET | `/health` | `{ "status": "OK" }` |
| GET | `/api/products?page=1&pageSize=20` | Paged list |
| GET | `/api/products/{id}` | One product, or 404 |
| POST | `/api/products` | 201 + `Location`, or 400 if invalid |
| PUT | `/api/products/{id}` | Updated product, or 404 |
| DELETE | `/api/products/{id}` | 204, or 404 |

## Run everything with Docker Compose
```powershell
docker compose up --build
```
Then open `http://localhost:8080/health`. Compose starts PostgreSQL and Redis first and waits until both are **healthy** before starting the API.

## Run the tests
```powershell
dotnet test
```
- **Unit tests:** controller (with a fake service), service (in-memory database) and the exception middleware.
- **Integration tests:** real HTTP requests through the whole pipeline using `WebApplicationFactory`, with PostgreSQL swapped for an in-memory database.

## Docker image (multi-stage)
- `builder` stage: .NET SDK image, restores and publishes the app.
- `runner` stage: small `aspnet:10.0-alpine` image containing only the published output, running as a non-root user.

## CI (`.github/workflows/ci.yml`)
Runs on every pull request to `main`: restore, test, build in Release, then build the Docker image. If any step fails, the check fails and the pull request can't be merged (see branch protection below).

## Mapping to the task's wording
| Task wording (Node) | This project (.NET) |
|---|---|
| `node:alpine` runner | `mcr.microsoft.com/dotnet/aspnet:10.0-alpine` |
| compile TypeScript in builder | `dotnet publish` in builder |
| Jest + Supertest | xUnit + `WebApplicationFactory` |
| `npm ci` | `dotnet restore` |
| bonus: compose with API, Postgres, Redis and health checks | `docker-compose.yml` with `depends_on: condition: service_healthy` |
