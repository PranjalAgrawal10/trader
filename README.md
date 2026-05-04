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
| `frontend/Dockerfile` | Multi-stage Node build + nginx static server (Compose `web` service) |
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

Database host defaults to **localhost:3306** when you run the API on the host against a local MySQL; match **`ConnectionStrings__MySQL`** in `backend/src/Trader.Api/.env.development` (user, password, port). **Docker Compose** maps container MySQL to **`localhost:3307`** on the host (see `docker-compose.yml`) so it does not fight for **3306** with another MySQL install; set `Port=3307` in your connection string when using that Compose database from **`dotnet run`**. Services inside Compose (`api` → `mysql`) still use port **3306** internally.

With **`Database:Provider=MySQL`** (and not **IntegrationTesting**), the API runs **`Migrate()`** on startup when **`Database:ApplyMigrationsOnStartup`** is **true** (default in `appsettings.json`). In **Development** it also **creates the database** if missing. **Production** and **Docker** typically use an existing catalog (e.g. managed MySQL) and only apply pending migrations. Set **`Database__ApplyMigrationsOnStartup=false`** to disable automatic startup migrations. You can still apply migrations manually (`dotnet ef`, below).

When you **`dotnet build src/Trader.Api/Trader.Api.csproj`** (entry project — not **`dotnet build Trader.sln`**), **`dotnet ef database update`** runs by default (**`RunEfMigrationsOnBuild`** is **true** unless you pass **`-p:RunEfMigrationsOnBuild=false`**). That requires the **`dotnet-ef`** global tool and a reachable database. **Solution** and **`dotnet test`** builds skip this step. **Docker** **`dotnet publish`** uses **`/p:RunEfMigrationsOnBuild=false`**.

### 2. Apply EF Core migrations (optional)

From the `backend/` directory:

```bash
cd backend
dotnet ef database update --project src/Trader.Infrastructure --startup-project src/Trader.Api
```

Use this when you prefer a one-off migrate without building, or when startup/build migrations are disabled. Migrations live under `backend/src/Trader.Infrastructure/Migrations/`.

**To skip the post-build step** when building the API project only:

```bash
cd backend
dotnet build src/Trader.Api/Trader.Api.csproj -p:RunEfMigrationsOnBuild=false
```

### 3. Run the API

```bash
cd backend
dotnet run --project src/Trader.Api
```

- Default HTTP URL: `http://localhost:5232` (see `Properties/launchSettings.json`).
- Swagger (Development): `/swagger`.
- Health: `GET /health`.
- API routes are versioned (e.g. `GET /api/v1/...`).

Configuration merges `appsettings*.json` with **process environment variables**. Optional **`.env`** files are merged **only** when `ASPNETCORE_ENVIRONMENT=Development` (`DotEnvBootstrap`); **Production never reads `.env`**. **Integration tests** use `appsettings.IntegrationTesting.json` + an in-memory database and skip `.env`.

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

## Docker Compose (full stack)

From the **repository root**:

```bash
docker compose up --build
```

| Service | Host URL / port | Notes |
|---------|-----------------|--------|
| **web** (SPA) | **http://localhost:8080** | nginx serves `frontend` production build; client calls API at **`http://localhost:5232`** (see `web.build.args.VITE_API_BASE_URL`). |
| **api** | **http://localhost:5232** | Swagger: `/swagger`, health: `/health`. |
| **mysql** | **localhost:3307** → container `3306` | Change if **3307** is taken. API inside Compose still uses `mysql:3306`. |
| **redis** | **localhost:6379** | Reserved for future use. |

CORS in Compose allows **`http://localhost:8080`** (Docker UI) and **`http://localhost:5173`** (optional local `npm run dev`).

To use a different API port or hostname, rebuild **`web`** with another build-arg, e.g. in `docker-compose.yml`: `VITE_API_BASE_URL: http://localhost:YOUR_PORT`.

`redis` is included for future use; the current API code does not require it.

## Configuration

### API (`backend/src/Trader.Api`)

| Mechanism | Notes |
|-----------|--------|
| `appsettings.json` | Structure and non-secret defaults (many values empty by design) |
| `appsettings.Development.json` | Development logging |
| `.env` / `.env.development` / `.env.local` | **Development only.** Merged by `DotEnvBootstrap` when `ASPNETCORE_ENVIRONMENT=Development`. Use `__` in keys (e.g. `ConnectionStrings__MySQL` or `Database__Name` / `Database__Password`). **Production ignores these files** — use platform env vars or `appsettings.Production.json` (non-secrets only). |
| `.env.production` (committed) | **Template / documentation only** for humans; the API does **not** load it at runtime in Production. |
| Environment variables | Override files (e.g. in Docker/Kubernetes). **DATABASE_URL** with **mysql://** is converted automatically (see `MySqlConnectionStringResolver`). |

See `backend/src/Trader.Api/.env.example`. Local overrides: **`.env.local`** / **`.env.development.local`** (gitignored). **Production** uses only **environment variables** and appsettings — **`.env` / `.env.production` are never loaded** by the host when `ASPNETCORE_ENVIRONMENT` is not `Development`. Blank values in merged `.env` lines are ignored.

Required for a real MySQL run: **`Database:Provider`**, then either **ADO.NET** **`ConnectionStrings:MySQL`**, or **`DATABASE_URL`** / **`ConnectionStrings__MySQL`** as **`mysql://user:pass@host:port/db?ssl-mode=REQUIRED`** (converted for MySqlConnector), or all of **`Database:Host`**, **`Database:Name`**, **`Database:UserId`**, **`Database:Password`**, plus **JWT** and **CORS** as before.

**DigitalOcean Managed MySQL** uses a non-default port (often **25060**) and requires TLS. Use a full connection string with **`SslMode=Required`**, **or** set **`Database__SslMode=Required`**, **`Database__Port=25060`**, and the other **`Database__*`** fields (see `.env.example`).

**DigitalOcean App Platform** (and similar reverse proxies): configure the API component **environment variables** at least: **`Jwt__Issuer`**, **`Jwt__Audience`**, **`Jwt__Key`** (UTF-8 secret ≥ 32 bytes), **`Cors__Origins__0`** (your SPA URL), **`Database__Provider=MySQL`**, **`ASPNETCORE_ENVIRONMENT=Production`**, and **`ConnectionStrings__MySQL`**, **`DATABASE_URL`** (**`mysql://…`**), **or** **`Database__Host`**, **`Database__Name`**, **`Database__UserId`**, **`Database__Password`** (optional **`Database__Port`**, **`Database__SslMode`**; use **`Required`** for managed MySQL). The API enables **`X-Forwarded-*`** headers so HTTPS termination at the edge works with **`UseHttpsRedirection`**. **Data Protection** (used for broker token encryption): set **`DataProtection__KeyRingPath`** to a **persisted** directory (e.g. App Platform mounted volume) so keys survive redeploys. If that is unset in Production, the app uses an in-memory key ring (no filesystem warnings; encrypted payloads still become invalid after a process restart). **Do not commit** key directories.

**404 on production (SPA + API on App Platform)** usually means routing or client-side routing:

1. **API calls return 404** — the browser requests `https://<your-app>/api/v1/...`. If ingress sends `/api` traffic to the static site, or **strips** the `/api` prefix before the .NET service, Kestrel sees the wrong path (for example `/v1/...` instead of `/api/v1/...`) and returns 404. Fix in the app **ingress** (Settings → your app → **Ingress** or edit the app spec): add a rule with path prefix **`/api`** pointing at your **Web Service** (API) component and set **`preserve_path_prefix: true`**. List the **`/api` rule before** the catch‑all **`/`** rule that serves the **Static Site**. Then set **`frontend/.env.production`** — or the static site’s **BUILD_TIME** env **`VITE_API_BASE_URL`** — to the **same public origin** as the SPA when both are one hostname (optional if you rely on same-origin fallback in `frontend/src/api/client.ts`). No path segment; the client appends `/api/v1`.
2. **Refreshing a deep link (e.g. `/brokers`) returns 404** — the static host has no file at that path. In the **Static Site** component, under **Custom Pages**, set **Catchall** to **`index.html`** (see [Manage static sites — Custom Pages](https://docs.digitalocean.com/products/app-platform/how-to/manage-static-sites/)). The web build also emits **`404.html`** (copy of `index.html`) for hosts that use a custom error page.

#### Zerodha Kite Connect

1. Create a Kite Connect app at [developers.kite.trade](https://developers.kite.trade/).
2. Set the **Redirect URL** in the developer console to exactly the same value as **`ZerodhaKite:RedirectUrl`** (e.g. dev API: `http://localhost:5232/api/v1/broker/kite/callback`). Mismatches cause OAuth failures.
3. In `.env.development` (or environment variables), set **`ZerodhaKite__ApiKey`**, **`ZerodhaKite__ApiSecret`**, and **`ZerodhaKite__RedirectUrl`**. Optionally override **`ZerodhaKite__PostLoginRedirectUrl`** if your SPA runs on a different origin than `http://localhost:5173/brokers`.
4. Apply EF migrations so `Users` includes Kite token columns (**`Migrate()`** on API startup for MySQL, post-build when you **`dotnet build`** the API project directly, or `dotnet ef database update`).

The API exchanges the `request_token` at Kite’s token endpoint and stores **encrypted** access (and refresh when present) tokens using ASP.NET Core Data Protection. **Do not commit API secrets**; keep them in local env files or a secrets manager.

- **Instruments (F&O + MCX)**: with a valid Zerodha-linked session, `GET /api/v1/broker/kite/instruments/fno-commodities` returns the full NFO, BFO, and MCX instrument rows from Kite’s daily CSV dumps (large payload; requires `Authorization: Bearer …`).

### Web (`frontend/`)

| File | Purpose |
|------|---------|
| `.env.development` | `VITE_API_BASE_URL`, optional `VITE_DEV_SERVER_PORT`, `VITE_API_PROXY_TARGET` |
| `.env.production` | Optional: production API origin. If unset in the production **build**, the client uses **`window.location.origin`** when the SPA and API share the same host and ingress serves `/api` on that host. |

See `frontend/.env.example`. Only variables prefixed with `VITE_` are exposed to the browser.

The SPA uses **React Bootstrap** (components) and **Bootstrap 5** (CSS); `index.html` sets `data-bs-theme="dark"`.

Example layout for production: see **[Deploy to DigitalOcean App Platform](#deploy-to-digitalocean-app-platform)** and **`.do/app.yaml`**.

## Deploy to DigitalOcean App Platform

High-level path: **Managed MySQL** + **one App** from this monorepo with a **Web Service** (API) and a **Static Site** (frontend), plus **ingress** so **`/api` → API** and **`/` → static**. TLS is at the edge; the .NET container listens on HTTP **8080** internally (match **`http_port`** in the spec).

### 1. Database

- Create a **Managed MySQL** cluster (same region as the app helps latency).
- Note **host**, **port** (often **25060**), **database**, **user**, **password**; use TLS (**`SslMode=Required`** in the connection string — see [Configuration](#configuration)).

### 2. Source control

- Push this repo to **GitHub** (or GitLab). Edit **`.do/app.yaml`**: set **`github.repo`**, **`branch`**, **`region`**, and instance sizes as needed.

### 3. Create / update the app

- **Apps → Create** → GitHub → pick repo. Paste or import the spec from **`.do/app.yaml`**, or after the first wizard pass use **Settings → App Spec** / **`doctl apps update --spec .do/app.yaml`**.
- Ensure **ingress** matches the file: **`/api` first** with **`preserve_path_prefix: true`** on the **Web Service** named **`trader`** (or rename consistently), then **`/`** to static **`trader-web`**.

### 4. API environment variables (Web Service `trader`)

Add under the service (encrypt secrets in the control panel):

| Key | Notes |
|-----|--------|
| **`ASPNETCORE_ENVIRONMENT`** | `Production` |
| **`Database__Provider`** | `MySQL` |
| **`ConnectionStrings__MySQL`** | **Option A:** ADO.NET string **or** **`mysql://user:pass@host:port/db?ssl-mode=REQUIRED`**. |
| **`DATABASE_URL`** | Optional alternative to **ConnectionStrings__MySQL** when the value is **`mysql://…`** (typical for managed MySQL). |
| **`Database__Host`**, **`Database__Name`**, **`Database__UserId`**, **`Database__Password`** | **Option B** when the full connection string is not set. **Encrypt** password (and any sensitive values). Optional: **`Database__Port`** (e.g. `25060`), **`Database__SslMode`** (e.g. `Required` for managed MySQL). |
| **`Jwt__Issuer`**, **`Jwt__Audience`**, **`Jwt__Key`** | **`Jwt__Key`** ≥ 32 chars; **secret**. |
| **`Cors__Origins__0`** | Your live site origin, e.g. **`https://your-app.ondigitalocean.app`** (no trailing slash). |
| **`ZerodhaKite__*`** | If you use Kite: **secrets**; **`RedirectUrl`** must be the public **`https://…/api/v1/broker/kite/callback`**. |

Optional: **`DataProtection__KeyRingPath`** if you attach **persistent storage** so broker encryption keys survive redeploys.

### 5. Frontend (Static Site `trader-web`)

- **Build command:** `npm install && npm run build` · **Output directory:** **`dist`** · **Source directory:** **`frontend`** (monorepo).
- **Custom pages:** **Catchall** = **`index.html`** (required for client-side routes).
- **`VITE_API_BASE_URL` (BUILD_TIME):** Set to your **public HTTPS origin** (e.g. `https://your-app.ondigitalocean.app`) if the SPA and API share one hostname—**or** omit and rely on the client’s same-origin fallback. If the API is **only** on another URL, set that origin here.

### 6. Database schema

- Run once from your machine (or a CI job) against production:

  ```bash
  cd backend
  dotnet ef database update --project src/Trader.Infrastructure --startup-project src/Trader.Api
  ```

  Use the same database configuration as production (**`ConnectionStrings__MySQL`** or **`Database__*`** fields) so migrations hit the managed DB. **Image builds** use **`/p:RunEfMigrationsOnBuild=false`**; **production** still applies migrations **on API startup** unless you set **`Database__ApplyMigrationsOnStartup=false`**.

### 7. Smoke test

- **`GET /api/health`** (or **`GET /health`** if you call the API service directly). On a **single hostname** with ingress, **`/health`** alone often hits the **static site**, so prefer **`/api/health`** for checks through the edge.
- Open the static URL, sign in, confirm **`/api/v1`** calls succeed (browser devtools **Network** tab).
- If **`/api`** routes return **404**, fix ingress (**`preserve_path_prefix`**) and rule order. If deep links **404**, set the static site **Catchall** to **`index.html`**.

**Troubleshooting — `JWT is not configured` / readiness `connection refused` on 8080:** The API process crashes **before** Kestrel listens, so probes fail. Add **`Jwt__Issuer`**, **`Jwt__Audience`**, and **`Jwt__Key`** (≥ 32 characters) to the **Web Service** component with scope **RUN_TIME** (not only BUILD_TIME, and not only on the static site). Add **`Cors__Origins__0`** the same way or the next startup error will be about CORS. Names must use **`__`** (e.g. **`Jwt__Key`**), not colons.

**Troubleshooting — MySQL connection / configuration missing:** Set **`ConnectionStrings__MySQL`** (ADO.NET), **`DATABASE_URL`** (**`mysql://…`**), **or** **`Database__Host`**, **`Database__Name`**, **`Database__UserId`**, **`Database__Password`** on the **Web Service** (**RUN_TIME**, encrypt secrets). If you see **Format of the initialization string…** or a problem value starting with **`$`**, you may have a **literal placeholder** (e.g. **`${DATABASE_URL}`**) that the platform did not substitute — paste the real connection URL/string, or use a proper **database reference** so **`DATABASE_URL`** is injected at runtime. The app also tries later sources if **`ConnectionStrings__MySQL`** is only a template.

**Troubleshooting — App Platform / Heroku build: `MSB4018` / `GenerateDepsFile` / `deps.json` in use:** The buildpack runs **`dotnet publish`** with a shared **`--artifacts-path`**, which can deadlock or race when MSBuild compiles shared projects on multiple nodes. This repo sets **`BuildInParallel=false`** in **`backend/Directory.Build.props`** so publish is single-threaded (slightly slower, stable on App Platform).

**Troubleshooting — 404 on the app URL:** The main **HTTPS URL** usually serves the **SPA** at **`/`**. Real API routes are under **`/api/v1/...`**. **`/swagger`** is disabled in Production unless you set **`Swagger__Enabled=true`** (see `appsettings.Production.json`). Hitting **`GET /`** on the API (when the platform routes it) returns a small JSON index; **`GET /api/health`** should return **`{"status":"ok"}`** when ingress sends **`/api`** to the API with **`preserve_path_prefix`**.

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
