# Trader platform — development plan

This repository hosts the **API gateway** (ASP.NET Core), **web dashboard** (React + TypeScript), and infrastructure stubs for PostgreSQL and Redis. The Python trader bot is planned as a separate service (see phases below).

## Repository layout

| Path | Role |
|------|------|
| `Trader.sln` | .NET solution |
| `src/Trader.Domain` | Entities and enums |
| `src/Trader.Application` | Use cases, DTOs, repository/service interfaces |
| `src/Trader.Infrastructure` | EF Core (`TraderDbContext`), PostgreSQL + InMemory provider switch, JWT + password hashing |
| `src/Trader.Api` | HTTP API: versioning (`api/v1/...`), JWT auth, Swagger, controllers |
| `tests/Trader.Tests` | xUnit + `WebApplicationFactory` smoke test (`IntegrationTesting` + InMemory DB) |
| `apps/web` | Vite 5 + React 18 + Tailwind 3 + Zustand + Axios + Recharts |
| `docker-compose.yml` | PostgreSQL + Redis (+ optional API image) |
| `docker/Dockerfile.api` | Multi-stage build for `Trader.Api` |

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Node.js](https://nodejs.org/) 18+ (20 LTS recommended)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (for PostgreSQL/Redis)
- `dotnet-ef` tools (once): `dotnet tool install --global dotnet-ef`

## Local development — database

1. Start PostgreSQL (matches default connection string):

   ```bash
   docker compose up -d postgres
   ```

2. Apply EF migrations:

   ```bash
   dotnet ef database update --project src/Trader.Infrastructure --startup-project src/Trader.Api
   ```

3. Override secrets in `src/Trader.Api/appsettings.Development.json` or user secrets: **`Jwt:Key`** must be at least 32 characters for HMAC-SHA256.

## Local development — API

```bash
dotnet run --project src/Trader.Api
```

- Swagger: `http://localhost:5232/swagger` (HTTP profile; see `launchSettings.json`).
- Health: `GET http://localhost:5232/health`.

## Local development — web UI

```bash
cd apps/web
npm install
npm run dev
```

- Dev server: `http://localhost:5173`
- API base URL: `apps/web/.env.development` → `VITE_API_BASE_URL=http://localhost:5232`

## Configuration switches

| Setting | Purpose |
|---------|---------|
| `Database:Provider` = `PostgreSQL` | Npgsql (default for real runs) |
| `Database:Provider` = `InMemory` | EF InMemory (used under `ASPNETCORE_ENVIRONMENT=IntegrationTesting`) |
| `appsettings.IntegrationTesting.json` | Enables InMemory DB for automated tests |

## Phase-aligned backlog (C# + React)

### Phase 1 — MVP (current baseline)

- **Backend:** Auth (register/login + JWT), strategies CRUD, bots lifecycle (create / assign strategy / start / stop), trades read API; clean layering; API versioning; Swagger + bearer auth.
- **Frontend:** Login/register, dashboard metrics, strategies editor (JSON params), bots control panel, trades table, basic price sparkline (Recharts).
- **Ops:** Docker Compose for Postgres/Redis; EF migrations checked in under `src/Trader.Infrastructure/Persistence/Migrations`.
- **Tests:** Integration smoke test for `/health` with InMemory DB.

### Phase 2 — Integration & paper trading

- Outbound **Trader Bot client** from API (REST or Redis pub/sub): publish commands, ingest fills/logs idempotently.
- **Redis** caching/pub-sub in Infrastructure; correlation IDs across API ↔ bot.
- Frontend: broker/paper mode badges, connection status, richer trade/order detail.

### Phase 3 — Risk & analytics

- Portfolio-level limits; audit trail UI; strategy versioning; backtest results APIs feeding charts.

### Phase 4 — Scale

- Queue-backed workers; horizontal bot runners; split deployables if needed.

## Verification commands

```bash
dotnet build Trader.sln
dotnet test Trader.sln
cd apps/web && npm run build
```

## Next service (not in this scaffold)

Add **`services/bot`** (Python/FastAPI) per your original architecture: market data, strategy engine, execution, risk module, and callbacks to this API’s trade/order endpoints.
