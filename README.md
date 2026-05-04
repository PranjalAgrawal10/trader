# Trader

This repository contains **two deployable projects**: **`backend/`** (.NET 8 API) and **`frontend/`** (Vite + React). They can stay in one Git repo or be pushed to **separate remotes** (copy each folder into its own repository root if you split).

Monorepo overview: REST API with **JWT**, **EF Core** + **MySQL**, and a **React (TypeScript)** UI for strategies, bots, trades, and broker onboarding.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [**dotnet-ef** global tool](https://learn.microsoft.com/ef/core/cli/dotnet) if you rely on **post-build migrations** from `Trader.Api` (or run `dotnet ef` manually): `dotnet tool install --global dotnet-ef`
- [Node.js](https://nodejs.org/) 18+ (for `frontend/`)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (optional; recommended for MySQL via Compose)

## Repository layout

| Path | Purpose |
|------|---------|
| `backend/Trader.sln` | .NET solution |
| `backend/src/Trader.Api` | ASP.NET Core host, controllers (`/api/v1/...`), Swagger, CORS |
| `backend/src/Trader.Application` | Use cases, DTOs, abstractions (`Abstractions/Persistence`, `Abstractions/Security`) |
| `backend/src/Trader.Domain` | Entities and enums |
| `backend/src/Trader.Infrastructure` | EF Core (`TraderDbContext`), MySQL (Pomelo), repositories, migrations |
| `frontend/` | Vite + React SPA |
| `backend/tests/Trader.Tests` | Integration tests (`WebApplicationFactory`) |
| `backend/docker/` | API Dockerfile |
| `docker-compose.yml` | MySQL, Redis (service only), API (repo root; build context `backend/`) |

To use **two separate Git repositories**, copy **`backend/`** contents to the backend repo root (so `Trader.sln` is at that root) and **`frontend/`** contents to the frontend repo root (so `package.json` is at that root). Point **DigitalOcean** `source_dir` to **`/`** for each app and keep the same ingress pattern, or deploy from this monorepo as configured in **`.do/app.yaml`**.

## Quick start (local)

### 1. Database (MySQL)

Start MySQL locally or via Docker. Example using Compose (MySQL only):

```bash
docker compose up mysql -d
```

Database host defaults to **localhost:3306**; user, password, and database name are set in `backend/src/Trader.Api/.env.development` (`ConnectionStrings__MySQL`). Adjust to match your MySQL install (Docker Compose still uses the `trader` MySQL user unless you change Compose too).

With **`ASPNETCORE_ENVIRONMENT=Development`** and **`Database:Provider=MySQL`**, the API **creates the database if it does not exist** and runs **`Migrate()`** on startup so tables stay up to date. You can still apply migrations manually (below) if you prefer.

### 2. Apply EF Core migrations (optional in Development)

From the `backend/` directory:

```bash
cd backend
dotnet ef database update --project src/Trader.Infrastructure --startup-project src/Trader.Api
```

Use this for **Production** or whenever you want to migrate without building/running the API. Migrations live under `backend/src/Trader.Infrastructure/Migrations/`.

Optionally, you can run **`dotnet ef database update`** automatically after every **`dotnet build`** of **`Trader.Api`** by opting in (requires the **`dotnet-ef`** global tool and a reachable DB per your env):

```bash
cd backend
dotnet build -p:RunEfMigrationsOnBuild=true
```

**Default is off** so **`dotnet publish`**, CI, and hosts like DigitalOcean App Platform (no `dotnet-ef` on PATH) succeed without extra configuration.

### 3. Run the API

```bash
cd backend
dotnet run --project src/Trader.Api
```

- Default HTTP URL: `http://localhost:5232` (see `Properties/launchSettings.json`).
- Swagger (Development): `/swagger`.
- Health: `GET /health`.
- API routes are versioned (e.g. `GET /api/v1/...`).

Configuration merges `appsettings*.json` with optional **`.env`** / **`.env.<environment>`** files under `backend/src/Trader.Api` (see [Configuration](#configuration)). **Integration tests** use `appsettings.IntegrationTesting.json` + an in-memory database and do not load `.env`.

### 4. Run the web app

```bash
cd frontend
npm install
npm run dev
```

- Dev server uses `.env.development` (`VITE_*` variables).
- Set `VITE_API_BASE_URL` to your API origin (e.g. `http://localhost:5232`). The client calls `{VITE_API_BASE_URL}/api/v1`.

Production build:

```bash
cd frontend
npm run build
```

Uses `.env.production` for `VITE_API_BASE_URL`.

## Docker Compose (API + MySQL)

Full stack (API container listens on **8080**, mapped to host **5232**):

```bash
docker compose up --build
```

The `api` service sets connection string host `mysql`, JWT, CORS, etc. The UI still runs locally with `npm run dev` unless you containerize it separately.

`redis` is included for future use; the current API code does not require it.

## Configuration

### API (`backend/src/Trader.Api`)

| Mechanism | Notes |
|-----------|--------|
| `appsettings.json` | Structure and non-secret defaults (many values empty by design) |
| `appsettings.Development.json` | Development logging |
| `.env` / `.env.development` / `.env.production` | Optional; merged into configuration. Use `__` in keys (e.g. `ConnectionStrings__MySQL`); the API maps these to nested keys (`ConnectionStrings:MySQL`), matching OS environment-variable conventions. |
| Environment variables | Override files (e.g. in Docker/Kubernetes) |

See `backend/src/Trader.Api/.env.example`. Local overrides can go in `.env.local` (gitignored). Values loaded from `.env` / `.env.*` are merged after default configuration; **blank values in those files are ignored** so they do not wipe out real environment variables on hosts like App Platform.

Required for a real MySQL run: **`Database:Provider`**, **`ConnectionStrings:MySQL`**, **JWT** (`Issuer`, `Audience`, `Key` ≥ 32 chars), and **CORS** origins (`Cors:Origins` or `Cors__Origins__0`, etc.).

**DigitalOcean Managed MySQL** uses a non-default port (often **25060**) and requires TLS. Use a .NET-style connection string with **`SslMode=Required`** (not the `mysql://…?ssl-mode=REQUIRED` URL). Example: `Server=…;Port=25060;Database=defaultdb;User Id=doadmin;Password=…;SslMode=Required;` — set the password via env on the host, not in committed files.

**DigitalOcean App Platform** (and similar reverse proxies): configure the API component **environment variables** at least: **`Jwt__Issuer`**, **`Jwt__Audience`**, **`Jwt__Key`** (UTF-8 secret ≥ 32 bytes), **`Cors__Origins__0`** (your SPA URL), **`ConnectionStrings__MySQL`**, **`Database__Provider=MySQL`**, **`ASPNETCORE_ENVIRONMENT=Production`**. The API enables **`X-Forwarded-*`** headers so HTTPS termination at the edge works with **`UseHttpsRedirection`**. **Data Protection** (used for broker token encryption): set **`DataProtection__KeyRingPath`** to a **persisted** directory (e.g. App Platform mounted volume) so keys survive redeploys. If that is unset in Production, the app uses an in-memory key ring (no filesystem warnings; encrypted payloads still become invalid after a process restart). **Do not commit** key directories.

**404 on production (SPA + API on App Platform)** usually means routing or client-side routing:

1. **API calls return 404** — the browser requests `https://<your-app>/api/v1/...`. If ingress sends `/api` traffic to the static site, or **strips** the `/api` prefix before the .NET service, Kestrel sees the wrong path (for example `/v1/...` instead of `/api/v1/...`) and returns 404. Fix in the app **ingress** (Settings → your app → **Ingress** or edit the app spec): add a rule with path prefix **`/api`** pointing at your **Web Service** (API) component and set **`preserve_path_prefix: true`**. List the **`/api` rule before** the catch‑all **`/`** rule that serves the **Static Site**. Then set **`frontend/.env.production`** — or the static site’s **BUILD_TIME** env **`VITE_API_BASE_URL`** — to the **same public origin** as the SPA when both are one hostname (optional if you rely on same-origin fallback in `frontend/src/api/client.ts`). No path segment; the client appends `/api/v1`.
2. **Refreshing a deep link (e.g. `/brokers`) returns 404** — the static host has no file at that path. In the **Static Site** component, under **Custom Pages**, set **Catchall** to **`index.html`** (see [Manage static sites — Custom Pages](https://docs.digitalocean.com/products/app-platform/how-to/manage-static-sites/)). The web build also emits **`404.html`** (copy of `index.html`) for hosts that use a custom error page.

#### Zerodha Kite Connect

1. Create a Kite Connect app at [developers.kite.trade](https://developers.kite.trade/).
2. Set the **Redirect URL** in the developer console to exactly the same value as **`ZerodhaKite:RedirectUrl`** (e.g. dev API: `http://localhost:5232/api/v1/broker/kite/callback`). Mismatches cause OAuth failures.
3. In `.env.development` (or environment variables), set **`ZerodhaKite__ApiKey`**, **`ZerodhaKite__ApiSecret`**, and **`ZerodhaKite__RedirectUrl`**. Optionally override **`ZerodhaKite__PostLoginRedirectUrl`** if your SPA runs on a different origin than `http://localhost:5173/brokers`.
4. Apply EF migrations so `Users` includes Kite token columns (startup **`Migrate()`** in Development, or `dotnet ef database update`).

The API exchanges the `request_token` at Kite’s token endpoint and stores **encrypted** access (and refresh when present) tokens using ASP.NET Core Data Protection. **Do not commit API secrets**; keep them in local env files or a secrets manager.

- **Instruments (F&O + MCX)**: with a valid Zerodha-linked session, `GET /api/v1/broker/kite/instruments/fno-commodities` returns the full NFO, BFO, and MCX instrument rows from Kite’s daily CSV dumps (large payload; requires `Authorization: Bearer …`).

### Web (`frontend/`)

| File | Purpose |
|------|---------|
| `.env.development` | `VITE_API_BASE_URL`, optional `VITE_DEV_SERVER_PORT`, `VITE_API_PROXY_TARGET` |
| `.env.production` | Optional: production API origin. If unset in the production **build**, the client uses **`window.location.origin`** when the SPA and API share the same host and ingress serves `/api` on that host. |

See `frontend/.env.example`. Only variables prefixed with `VITE_` are exposed to the browser.

The SPA uses **React Bootstrap** (components) and **Bootstrap 5** (CSS); `index.html` sets `data-bs-theme="dark"`.

Example **DigitalOcean App Platform** layout: **`.do/app.yaml`** (**`source_dir`**: **`backend`** for the API, **`frontend`** for the static site; ingress **`/api` →** API, **`/` →** static site). Set **`VITE_API_BASE_URL`** at **BUILD_TIME** when the API lives on a different origin.

## Testing

```bash
cd backend
dotnet test
```

Uses environment **IntegrationTesting** and EF Core **InMemory** database.

## Architecture notes

- **Layering**: Domain → Application → Infrastructure; API references Application + Infrastructure and wires DI.
- **SOLID-oriented ports**: persistence interfaces under `Trader.Application.Abstractions.Persistence`; broker onboarding uses `IBrokerSetupGateway` instead of pulling the full user repository into broker services.
- **Errors**: API maps domain/application failures to **ProblemDetails** via `ApplicationExceptionFilter` (e.g. `ConflictException` → 409).

## License

Specify your license here if applicable.
