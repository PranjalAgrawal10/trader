# Trader

This repository contains **two deployable projects**: **`backend/`** (.NET 8 API) and **`frontend/`** (Vite + React). They can stay in one Git repo or be pushed to **separate remotes** (copy each folder into its own repository root if you split).

Monorepo overview: REST API with **JWT**, **EF Core** + **MySQL**, and a **React (TypeScript)** UI for strategies, bots, trades, broker onboarding, **email verification**, **password reset links**, and **second-factor sign-in** (authenticator **TOTP** or **email OTP** after password).

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

Database host defaults to **localhost:3306** when you run the API on the host against a local MySQL; set **`Database__Host`**, **`Database__Port`**, **`Database__Name`**, **`Database__Username`**, **`Database__Password`** in `backend/src/Trader.Api/.env.development` (same shape as **`.env.example`**). **Docker Compose** maps container MySQL to **`localhost:3307`** on the host (see `docker-compose.yml`) so it does not fight for **3306** with another MySQL install; use **`Database__Port=3307`** when using that Compose database from **`dotnet run`** on the host. Services inside Compose (`api` → `mysql`) still use port **3306** internally (`Database__Host=mysql`, **`Database__Port=3306`**).

With **`Database:Provider=MySQL`** (and not **IntegrationTesting**), the API runs **`Migrate()`** on startup when **`Database:ApplyMigrationsOnStartup`** is **true** (default in `appsettings.json`). In **Development** it also **creates the database** if missing. **Production** and **Docker** typically use an existing catalog (e.g. managed MySQL) and only apply pending migrations. Set **`Database__ApplyMigrationsOnStartup=false`** to disable automatic startup migrations. You can still apply migrations manually (`dotnet ef`, below).

When you **`dotnet build`** the API from `backend/` in **Debug**, **`dotnet ef database update`** runs after the API project build by default (**`RunEfMigrationsOnBuild`** is **true**), which requires the **`dotnet-ef`** global tool and **reachable MySQL** (same **`Database__*`** / `.env` as **`dotnet run`**). For **Release** builds (including **`dotnet publish`** on hosts like DigitalOcean App Platform that do not install **`dotnet-ef`**), **`RunEfMigrationsOnBuild`** defaults to **false** so publish succeeds; rely on **`Migrate()`** on API startup (or run **`dotnet ef database update`** from a machine with the tool) unless you pass **`-p:RunEfMigrationsOnBuild=true`**. Use **`-p:RunEfMigrationsOnBuild=false`** when the database is unavailable during compile. **`dotnet test`** references the API with **`RunEfMigrationsOnBuild=false`**. **Docker** **`dotnet publish`** can still pass **`/p:RunEfMigrationsOnBuild=false`** explicitly.

### 2. Apply EF Core migrations (optional)

From the `backend/` directory:

```bash
cd backend
dotnet ef database update --project src/Trader.Infrastructure --startup-project src/Trader.Api
```

Use this when you prefer a one-off migrate without building, or when startup/build migrations are disabled. Migrations live under `backend/src/Trader.Infrastructure/Migrations/`.

**To skip the post-build step** for any build:

```bash
cd backend
dotnet build Trader.sln -p:RunEfMigrationsOnBuild=false
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
- **Realtime:** SignalR hub **`/hubs/market`** (same origin as the API). Authenticate with the SPA JWT via the **`access_token`** query parameter (the JS client uses `accessTokenFactory`). Hub methods: **`SubscribeInstrument`** / **`UnsubscribeInstrument`** (numeric Kite `instrument_token`). Server pushes batched **`ticks`** events: `[{ i, p, v, t? }]` (LTP mode). Requires **Zerodha** connected and a valid Kite session; the server opens one **Kite WebSocket** per user with active subscriptions (`Tech.Zerodha.KiteConnect` **4.3.0**). When **`LiveCandles:Enabled`** is **true** (default in `appsettings.json`; **false** in integration tests), those ticks also **aggregate into UTC 1-minute OHLCV** rows in **`HistoricalCandles`** (`Timeframe` **`1m`**); duplicate subscriptions across users **dedupe by instrument**. Turn off with **`LiveCandles__Enabled=false`** if you use a non-MySQL provider without raw SQL upserts.

**Authentication:** **`POST /api/v1/auth/register`** responds **`{ "email_verification_required": true }`** and emails a verification link (**`Smtp__*`**, **`PublicWeb__FrontendBaseUrl`** — see **`.env.example`**). **`POST /api/v1/auth/verify-email`** **`{ "token" }`** returns a JWT. **`POST /api/v1/auth/login`** returns **`{ "requires_email_verification": true }`** when the password is correct but email is not confirmed yet. Once verified, normal login returns a JWT or **`{ "requires_2fa": true, "temp_token": "…", "second_factor": "authenticator" | "email_otp" }`** when a second factor is enabled; complete with **`POST /api/v1/2fa/verify-login`**. Use **`POST /api/v1/auth/resend-login-otp`** for **`email_otp`**. **`GET /api/v1/auth/me`** includes **`email_verified`**. **Password reset:** **`POST /api/v1/auth/forgot-password`**, **`POST /api/v1/auth/reset-password`**. **`POST /api/v1/2fa/enable-email-sign-in`** (Bearer) turns on email codes; **`GET /api/v1/2fa/status`** includes **`second_factor_method`**.
**Verify-login `otp`** is a TOTP or recovery code when **`second_factor`** is **`authenticator`**, and the emailed six-digit code when **`email_otp`**. Repeated failures lock the step (**`Auth:MaxFailedTotpAttemptsPerScope`**, **`Auth:TotpAttemptLockoutMinutes`**). Bearer **Enrollment** routes: **`2fa/setup`**, **`verify-setup`** (issues **`recovery_codes`** once), **`cancel-setup`**, **`disable`** (password and/or OTP/recovery for authenticator mode; password-only to disable email OTP). Managed MySQL should run migrations including **`EmailOtpChallenges`**, **`AddUserEmailVerificationAndSecondFactor`**, **`AddKiteFavoriteInstruments`**, **`AddKiteInstrumentsChartSettings`**, **`AddKiteInstrumentsChartZoomJson`**, **`AddMlPriceDirectionPredictions`**, **`AddMlFavoriteEodReportSent`**, and **`AddBrokerAccountsAndHistoricalCandles`** ( **`BrokerAccounts`** + **`HistoricalCandles`**; Kite tokens move off **`Users`** ). Demo-only: **`POST /api/v1/auth/email-otp/*`**.

The SPA exposes **`/verify-email`**, **`/forgot-password`**, and **`/reset-password`** aligned with **`PublicWeb`** paths. The **`/profile`** page (**Account**, **Security** — email vs authenticator 2FA, **Broker**) is still the gate (`RequiresTwoFactor`, `RequiresBroker`). **`/security`** and **`/brokers`** redirects are unchanged.

Configuration merges `appsettings*.json` with **process environment variables**. Optional **`.env`** files are merged **only** when `ASPNETCORE_ENVIRONMENT=Development` (`DotEnvBootstrap`); **Production never reads `.env`**. **Integration tests** use `appsettings.IntegrationTesting.json` + an in-memory database and skip `.env`.

### 4. Run the web app

```bash
cd frontend
npm install
npm run dev
```

- Dev server uses `.env.development` (`VITE_*` variables).
- Set `VITE_API_BASE_URL` to your API origin (e.g. `http://localhost:5232`). The client calls `{VITE_API_BASE_URL}/api/v1`.
- On **Kite instruments** at **`/instruments`**, the chart toolbar includes **Candles**: green/red OHLC candlesticks with a **time axis** along the bottom and **hover** OHLC/VOL + MA details; **Zoom** (+ / − / Reset) can narrow the view to as few as **one** recent bar or reset to the full downloaded series; **Full screen** uses the browser’s fullscreen API on the zoom + plot panel (favorites tiles and **Browse** detail chart) and keeps range caption, ML controls, and (**Browse**) symbol / LTP / favorites, chart toolbar, and ML bar in a **scrollable top-left** strip above the chart; visible-bar count is **saved per instrument** (server) and restored when you reopen charts; overlays **SMA 20** (amber), **EMA 9** (violet), **EMA 21** (sky), and an optional **custom-period EMA** (orange, period 2–500, toggle + number input; preference in **localStorage**) on line, bar, and candle plots, each toggled under **Indicators** (local UI only). **Search Kite** runs the server file scan on **Enter** or button click whenever the query is non-empty (including when the capped preview already shows matches); **Browse** uses one **segment** per list (F&O vs MCX); **All favorites** merges **both** segment searches. **All favorites** chart tiles include the same **ML next-bar bias** control as **Browse** (calls `predictions/price-direction` per instrument; **history** for that bias is stored **per user** on the server). **Full predictions** (browser fullscreen) shows a **pie chart** of **correct** / **wrong** / **pending** counts and a **taller scrollable** history table. **Refresh chart** (next to zoom / fullscreen, or while a chart is still loading) and **↻ Predictions** reload OHLC and prediction history immediately. With Zerodha connected and a row selected on **Browse**, SignalR ticks update the **in-progress** bar for the chosen interval.

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

CORS in Compose allows **`http://localhost:8080`** (Docker UI) and **`http://localhost:5173`** (optional local `npm run dev`). **SignalR** (`/hubs/market`) uses WebSockets; ensure any **reverse proxy** in front of the API allows **upgrade** and long-lived connections for that path.

To use a different API port or hostname, rebuild **`web`** with another build-arg, e.g. in `docker-compose.yml`: `VITE_API_BASE_URL: http://localhost:YOUR_PORT`.

`redis` is included for future use; the current API code does not require it.

## Configuration

### API (`backend/src/Trader.Api`)

| Mechanism | Notes |
|-----------|--------|
| `appsettings.json` | Structure and non-secret defaults (many values empty by design) |
| `appsettings.Development.json` | Development logging |
| `.env` / `.env.development` / `.env.local` | **Development only.** Merged by `DotEnvBootstrap` when `ASPNETCORE_ENVIRONMENT=Development`. Use **`Database__Host`**, **`Database__Port`**, **`Database__Name`**, **`Database__Username`**, **`Database__Password`** as in **`.env.example`**. **Production ignores these files** — use platform env vars or `appsettings.Production.json` (non-secrets only). |
| `.env.production` (committed) | **Template / documentation only** for humans; the API does **not** load it at runtime in Production. |
| Environment variables | Override files. Discrete **`Database__*`** or **`MYSQL_*`** / **`DB_*`** aliases (see `MySqlConnectionStringResolver`) build the MySQL connection. |

See `backend/src/Trader.Api/.env.example`. Local overrides: **`.env.local`** / **`.env.development.local`** (gitignored). **Production** uses only **environment variables** and appsettings — **`.env` / `.env.production` are never loaded** by the host when `ASPNETCORE_ENVIRONMENT` is not `Development`. Blank values in merged `.env` lines are ignored.

Required for a real MySQL run: **`Database:Provider`**, then all of **`Database:Host`**, **`Database:Name`**, **`Database:Username`** (or **`UserId`**), **`Database:Password`** (optional **`Port`**, **`SslMode`**). Equivalent env aliases: **`MYSQL_HOST`**, **`MYSQL_DATABASE`**, **`MYSQL_USER`**, **`MYSQL_PASSWORD`** (and **`DB_*`** / **`DATABASE_*`** variants — see `MySqlConnectionStringResolver`). There is **no** `ConnectionStrings:MySQL` or **`DATABASE_URL`** path — only discrete fields. You still need **JWT** and **CORS**.

**DigitalOcean Managed MySQL** uses a non-default port (often **25060**) and requires TLS. Set **`Database__SslMode=Required`**, **`Database__Port=25060`**, and the other **`Database__*`** fields (see `.env.example`).

**DigitalOcean App Platform** (and similar reverse proxies): configure the API component **environment variables** at least: **`Jwt__Issuer`**, **`Jwt__Audience`**, **`Jwt__Key`** (UTF-8 secret ≥ 32 bytes), **`Cors__Origins__0`** (your SPA URL), **`Database__Provider=MySQL`**, **`ASPNETCORE_ENVIRONMENT=Production`**, and **`Database__Host`**, **`Database__Name`**, **`Database__Username`**, **`Database__Password`** (optional **`Database__Port`**, **`Database__SslMode=Required`** for managed MySQL). The API enables **`X-Forwarded-*`** headers so HTTPS termination at the edge works with **`UseHttpsRedirection`**. **Data Protection** (broker token and **TOTP secret** encryption): set **`DataProtection__KeyRingPath`** to a **persisted** directory (e.g. App Platform mounted volume) so keys survive redeploys. If that is unset in Production, the app uses an in-memory key ring (no filesystem warnings; encrypted payloads still become invalid after a process restart). **Do not commit** key directories.

**404 on production (SPA + API on App Platform)** usually means routing or client-side routing:

1. **API calls return 404** — the browser requests `https://<your-app>/api/v1/...`. If ingress sends `/api` traffic to the static site, or **strips** the `/api` prefix before the .NET service, Kestrel sees the wrong path (for example `/v1/...` instead of `/api/v1/...`) and returns 404. Fix in the app **ingress** (Settings → your app → **Ingress** or edit the app spec): add a rule with path prefix **`/api`** pointing at your **Web Service** (API) component and set **`preserve_path_prefix: true`**. List the **`/api` rule before** the catch‑all **`/`** rule that serves the **Static Site**. Then set **`frontend/.env.production`** — or the static site’s **BUILD_TIME** env **`VITE_API_BASE_URL`** — to the **same public origin** as the SPA when both are one hostname (optional if you rely on same-origin fallback in `frontend/src/api/client.ts`). No path segment; the client appends `/api/v1`.
2. **Refreshing a deep link (e.g. `/profile`, `/instruments`) returns 404** — the static host has no file at that path. In the **Static Site** component, under **Custom Pages**, set **Catchall** to **`index.html`** (see [Manage static sites — Custom Pages](https://docs.digitalocean.com/products/app-platform/how-to/manage-static-sites/)). The web build also emits **`404.html`** (copy of `index.html`) for hosts that use a custom error page.

#### Zerodha Kite Connect

1. Create a Kite Connect app at [developers.kite.trade](https://developers.kite.trade/).
2. Set the **Redirect URL** in the developer console to exactly the same value as **`ZerodhaKite__RedirectUrl`** (e.g. dev API: `http://localhost:5232/api/v1/broker/kite/callback`). Mismatches cause OAuth failures.
3. Set **`ZerodhaKite__ApiKey`**, **`ZerodhaKite__ApiSecret`**, and **`ZerodhaKite__RedirectUrl`** as **environment variables** (Production/Staging) or in **`.env.development`** / **`.env.development.local`** when `ASPNETCORE_ENVIRONMENT=Development` (merged into configuration by `DotEnvBootstrap`). **Do not** put these values in committed **`appsettings*.json`**. Optionally override **`ZerodhaKite__PostLoginRedirectUrl`** if your SPA runs on a different origin than the default **`http://localhost:5173/profile#broker-connection`** (where users return after OAuth; should open **Profile → Broker connection**).
4. Apply EF migrations so `Users` matches the model (e.g. **`Migrate()`** on API startup for MySQL, **`dotnet build`** with **`RunEfMigrationsOnBuild=true`**, or `dotnet ef database update`).

Successful redirects from Kite include **`request_token`**; a **`status=success`** query parameter is **not** always present. The API treats the callback as failed only if **`status`** is sent and is **not** `success`.

**Split SPA and API (two public hostnames)** — e.g. static site at `https://trader-fe-vumpy.ondigitalocean.app` and API at `https://trader-be-7cdnc.ondigitalocean.app` ([Trader Console](https://trader-fe-vumpy.ondigitalocean.app/) · [API root](https://trader-be-7cdnc.ondigitalocean.app/)):

| Where | Setting |
|--------|---------|
| **Frontend build** | **`VITE_API_BASE_URL`** = `https://trader-be-7cdnc.ondigitalocean.app` (no trailing slash). Required when the SPA origin ≠ API origin. |
| **API env** | **`Cors__Origins__0`** = `https://trader-fe-vumpy.ondigitalocean.app` (exact SPA origin; no trailing slash). |
| **Kite developer console** | **Redirect URL** = `https://trader-be-7cdnc.ondigitalocean.app/api/v1/broker/kite/callback` — must be the **API** host, not the static site. |
| **API env** | **`ZerodhaKite__RedirectUrl`** = same URL as in Kite console. |
| **API env** | **`ZerodhaKite__PostLoginRedirectUrl`** = `https://trader-fe-vumpy.ondigitalocean.app/profile#broker-connection` (SPA URL where users land after OAuth; `#broker-connection` scrolls to the broker card). |

If **Redirect URL** is registered on the frontend hostname, Zerodha will call the wrong service and linking will fail.

The API exchanges the `request_token` at Kite’s token endpoint and stores **encrypted** access (and refresh when present) tokens using ASP.NET Core Data Protection. The login URL also sets Kite **`redirect_params`** with **`trader_oauth=<short id>`** so Zerodha echoes it on the callback if the main **`state`** query is missing. The OAuth **`state`** sent to Zerodha is a **short id** mapped in **server memory** to the HMAC-signed payload (`AddMemoryCache`); this avoids long URLs being dropped. **Split SPA/API**: the HttpOnly fallback cookie uses **`SameSite=None`** and **`Secure`** on HTTPS so credentialed XHR from the SPA origin can store a cookie for the API host (still secondary to **`state` / `trader_oauth`**). **Scaling to multiple API instances** requires **session affinity** or a **shared distributed cache** for that mapping (single-instance App Platform is fine). OAuth **`state`** signing for the stored payload still uses **`Jwt:Key`**. Persist **`DataProtection__KeyRingPath`** so stored broker tokens and 2FA secrets keep working across deploys. **Do not commit API secrets**; keep them in local env files or a secrets manager.

- **Instruments (F&O + MCX)**: with a valid Zerodha-linked session, `GET /api/v1/broker/kite/instruments/fno-commodities` returns up to **100 rows per exchange** (NFO, BFO, MCX) from Kite’s daily CSV dumps, fetched in parallel; `fnoTruncated` / `commoditiesTruncated` indicate when more rows exist on Kite. **`GET /api/v1/broker/kite/instruments/search?q=…&segment=fno|mcx`** streams the same CSVs server-side and returns substring matches (NFO+BFO for `fno`, MCX for `mcx`), up to a capped row count; `scanTruncated` is true when the file may have more matches. **`GET /api/v1/broker/kite/historical-candles?instrumentToken=…&interval=…`** returns OHLCV from Kite’s historical API (`interval` codes: `1m`, `2m`, `3m`, `4m`, `5m`, `10m`, `15m`, `30m`, `1h`, `1d`; optional `from` / `to` as ISO instants, UTC), plus per-candle **`sma20`**, **`ema9`**, **`ema21`** (nullable, same definitions as the SPA). The server pulls additional bars **before** the requested window (by interval) so SMA/EMA lines are warmed through the first visible bar. **Favorite instruments** (saved rows for charts/lists): **`GET /api/v1/broker/kite/favorites`** returns `{ items: [ … ] }` (same shape as instrument rows); **`POST /api/v1/broker/kite/favorites`** with a JSON body matching that row adds a favorite (idempotent if already saved); **`DELETE /api/v1/broker/kite/favorites?instrumentToken=…`** removes it. Favorites are stored in **`KiteFavoriteInstruments`** (MySQL) per user. **Chart toolbar** (interval, range preset, line / bar / candles): **`GET /api/v1/broker/kite/instruments/chart-settings`** returns `{ interval, rangePreset, graphType, zoomByInstrumentToken }` (first three may be null until saved; **`zoomByInstrumentToken`** maps numeric Kite `instrument_token` strings to **visible bar count** for chart zoom, or null/omitted when empty); **`PUT /api/v1/broker/kite/instruments/chart-settings`** with `{ interval, rangePreset, graphType }` (all three required) persists those fields on **`Users`** without clearing saved zoom. **`PUT /api/v1/broker/kite/instruments/chart-zoom`** with `{ instrumentToken, visibleBars }` (`visibleBars` null removes zoom for that token) persists zoom per instrument. Requires `Authorization: Bearer …`.
- **ML prediction (experimental)**: **`GET /api/v1/predictions/price-direction?instrumentToken=…&interval=…`** (Bearer; same `interval` codes). Uses **ML.NET** (`SdcaLogisticRegression`) trained on-the-fly on recent closes (short-horizon returns + SMA gap). Response adds optional **`predictionId`**, **`refBarTime`**, **`refClose`**, **`predictedAt`** when the run is **persisted** (enough candles); otherwise those fields are null. **`GET /api/v1/predictions/price-direction/history?instrumentToken=…&interval=…&take=…`** returns stored rows (newest first, cap 2000). **`PATCH /api/v1/predictions/price-direction/{id}/resolve`** with JSON **`{ "nextBarTime", "nextClose" }`** sets **correct/wrong** vs the reference bar. Rows are capped per user (25k); oldest prune on insert. The SPA loads history from the API and syncs resolves; **`localStorage`** is only a fallback if the history GET fails; the history table keeps **about five** rows on screen and scrolls for the rest. Table **`MlPriceDirectionPredictions`**. Not investment advice; heuristic fallback if training fails or output is near 50%. **Optional automation:** when **`FavoriteMlAutomation:Enabled`** is **true** (off in default **`appsettings.json`**), a **background service** polls Kite for each user who has **favorite** instruments: it **resolves** pending rows when the next candle exists, then adds **at most one** pending prediction per **favorite** per **reference bar** (interval from saved chart settings or **`DefaultChartInterval`**). Users **without a valid Kite session** are skipped. If **`Smtp:IsEnabled`** is **true**, once per **local** day (**`ReportTimeZoneId`**, default **`Asia/Kolkata`**) starting at **`ReportLocalHour`/`ReportLocalMinute`** (default **20:00**, two-hour send window), the API emails that user a **pie chart PNG** and **CSV** for predictions whose **`PredictedAtUtc`** falls on that calendar day (**`MlFavoriteEodReportsSent`** prevents duplicates). See **`FavoriteMlAutomation`** in **`appsettings.json`** and **`.env.example`**.

### Web (`frontend/`)

SPA paths: **`/instruments?tab=favorites`** (or **`?tab=fav`**, **`?fav=1`**, **`?fav=true`**) opens the **All favorites** tab; **`/instruments/fav`** redirects there (same auth gates as **`/instruments`**). Switching tabs updates the query with **`replace`** so links stay shareable.

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
- Note **host**, **port** (often **25060**), **database**, **user**, **password**; use TLS (**`Database__SslMode=Required`** — see [Configuration](#configuration)).

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
| **`Database__Host`**, **`Database__Name`**, **`Database__Username`**, **`Database__Password`** | **Required.** **Encrypt** password. Optional: **`Database__Port`**, **`Database__SslMode`**. Aliases: **`MYSQL_HOST`**, **`MYSQL_DATABASE`**, **`MYSQL_USER`**, **`MYSQL_PASSWORD`** (also **`DB_*`** / **`DATABASE_*`** — see resolver). **`Database__UserId`** works instead of **`Username`**. |
| **`Jwt__Issuer`**, **`Jwt__Audience`**, **`Jwt__Key`** | **`Jwt__Key`** ≥ 32 chars; **secret**. |
| **`Jwt__ExpiresHours`** (optional) | Access token lifetime in hours (default **12** in **`appsettings.json`**). Shorter values (e.g. **1**) cause **`SecurityTokenExpiredException`** in logs after idle time; users must sign in again or you can raise this in production. |
| **`Cors__Origins__0`** | Your live site origin, e.g. **`https://your-app.ondigitalocean.app`** (no trailing slash). |
| **`ZerodhaKite__*`** | If you use Kite: **secrets**; **`RedirectUrl`** must be the public **`https://…/api/v1/broker/kite/callback`**. |

Optional: **`DataProtection__KeyRingPath`** if you attach **persistent storage** so **2FA and** broker encryption keys survive redeploys.

**Debugging HTTP 401 (JWT):** When a **Bearer** token is present but invalid, logs include **`Trader.Api.JwtBearer`** at **Warning** with the validation exception (e.g. wrong signature, issuer, audience, or expired). Production also sets **`Microsoft.AspNetCore.Authentication`** and **`JwtBearer`** to **Information** in **`appsettings.Production.json`** (adjust via **`Logging__LogLevel__*`** env vars on the host if needed). The SPA omits **Authorization** on anonymous auth routes (so an expired stored token does not block login/register) and clears the session and redirects to **`/login`** on **401** when a token was present.

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

  Use the same **`Database__*`** (or alias) variables as production so migrations hit the managed DB. **Image builds** use **`/p:RunEfMigrationsOnBuild=false`**; **production** still applies migrations **on API startup** unless you set **`Database__ApplyMigrationsOnStartup=false`**.

**Troubleshooting — `Unknown column '...KiteInstrumentsChartZoomJson'` (login/register/forgot-password 500):** The model includes **`AddKiteInstrumentsChartZoomJson`**, but the live **`Users`** table in the catalog the API uses (logs show **`TABLE_SCHEMA='defaultdb'`**) never got the column while **`__EFMigrationsHistory`** already lists that migration—so **`Migrate()`** reports *already up to date* and does nothing. Repair on that database (DigitalOcean MySQL console or any client), idempotent on MySQL 8+:

```sql
ALTER TABLE `Users`
  ADD COLUMN IF NOT EXISTS `KiteInstrumentsChartZoomJson` longtext NULL;
```

On older MySQL, run a plain **`ADD COLUMN`** once, or use **`SHOW COLUMNS FROM Users LIKE 'KiteInstrumentsChartZoomJson';`** to confirm before adding. Ensure you are connected to the **same** schema as **`Database__Name`** (managed clusters often use **`defaultdb`**).

### 7. Smoke test

- **`GET /api/health`** (or **`GET /health`** if you call the API service directly). On a **single hostname** with ingress, **`/health`** alone often hits the **static site**, so prefer **`/api/health`** for checks through the edge.
- Open the static URL, sign in, confirm **`/api/v1`** calls succeed (browser devtools **Network** tab).
- If **`/api`** routes return **404**, fix ingress (**`preserve_path_prefix`**) and rule order. If deep links **404**, set the static site **Catchall** to **`index.html`**.

**Troubleshooting — `JWT is not configured` / readiness `connection refused` on 8080:** The API process crashes **before** Kestrel listens, so probes fail. Add **`Jwt__Issuer`**, **`Jwt__Audience`**, and **`Jwt__Key`** (≥ 32 characters) to the **Web Service** component with scope **RUN_TIME** (not only BUILD_TIME, and not only on the static site). Add **`Cors__Origins__0`** the same way or the next startup error will be about CORS. Names must use **`__`** (e.g. **`Jwt__Key`**), not colons.

**Troubleshooting — MySQL connection / configuration missing:** On the **Web Service**, set **`Database__Host`**, **`Database__Name`**, **`Database__Username`**, **`Database__Password`** (**RUN_TIME**, encrypt the password), plus optional **`Database__Port`** / **`Database__SslMode`**. See **`backend/src/Trader.Api/.env.example`** for the canonical names.

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
