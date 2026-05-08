# Trader platform — development plan

This repository hosts the **API gateway** (ASP.NET Core under **`backend/`**), **web dashboard** (React + TypeScript under **`frontend/`**), and infrastructure stubs for PostgreSQL and Redis. The Python trader bot is planned as a separate service (see phases below).

## Repository layout

| Path | Role |
|------|------|
| `backend/Trader.sln` | .NET solution |
| `backend/src/Trader.Domain` | Entities and enums |
| `backend/src/Trader.Application` | Use cases, DTOs, repository/service interfaces |
| `backend/src/Trader.Infrastructure` | EF Core (`TraderDbContext`), PostgreSQL + InMemory provider switch, JWT + password hashing |
| `backend/src/Trader.Api` | HTTP API: versioning (`api/v1/...`), JWT auth, Swagger, controllers |
| `backend/tests/Trader.Tests` | xUnit + `WebApplicationFactory` smoke test (`IntegrationTesting` + InMemory DB) |
| `frontend/` | Vite 5 + React 18 + Zustand + Axios + Recharts |
| `docker-compose.yml` | PostgreSQL + Redis (+ optional API image); API build context `backend/` |
| `backend/docker/Dockerfile.api` | Multi-stage build for `Trader.Api` |

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
   cd backend
   dotnet ef database update --project src/Trader.Infrastructure --startup-project src/Trader.Api
   ```

3. Override secrets in `backend/src/Trader.Api/appsettings.Development.json` or user secrets: **`Jwt:Key`** must be at least 32 characters for HMAC-SHA256.

## Local development — API

```bash
cd backend
dotnet run --project src/Trader.Api
```

- Swagger: `http://localhost:5232/swagger` (HTTP profile; see `launchSettings.json`).
- Health: `GET http://localhost:5232/health`.

## Local development — web UI

```bash
cd frontend
npm install
npm run dev
```

- Dev server: `http://localhost:5173`
- API calls: same-origin `/api` and `/hubs`; Vite proxies them to the backend (`VITE_API_PROXY_TARGET`, default `http://localhost:5232`).

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
- **Ops:** Docker Compose for Postgres/Redis; EF migrations checked in under `backend/src/Trader.Infrastructure/Migrations`.
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
cd backend
dotnet build Trader.sln
dotnet test Trader.sln
cd ../frontend
npm run build
```

## Next service (not in this scaffold)

Add **`services/bot`** (Python/FastAPI) per your original architecture: market data, strategy engine, execution, risk module, and callbacks to this API’s trade/order endpoints.
