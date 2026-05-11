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

**Conventions:** root **`.editorconfig`** keeps C# / TS / YAML formatting aligned; **`AGENTS.md`** is a short map for humans and AI tools. **`backend/global.json`** pins the .NET 8 SDK band with `rollForward` so local and CI stay on the same major.minor. Backend layering and SOLID expectations are summarized in **`.cursor/rules/core-principles.mdc`** (see “SOLID in this repo” there).

## Continuous integration

On **GitHub**, **`.github/workflows/ci.yml`** runs on pushes and pull requests to **`main`** / **`master`**: backend **`dotnet build`** + **`dotnet test`** with **`-p:RunEfMigrationsOnBuild=false`**, and **`frontend/`** **`npm ci`**, **`npm run typecheck`**, **`npm run build`**. Enable Actions on the repo if you use GitHub.

### Local checks (no database required for compile)

```bash
cd backend
dotnet build Trader.sln -c Release -p:RunEfMigrationsOnBuild=false
dotnet test Trader.sln -c Release --no-build
```

```bash
cd frontend
npm run typecheck
npm run build
```

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
**Verify-login `otp`** is a TOTP or recovery code when **`second_factor`** is **`authenticator`**, and the emailed six-digit code when **`email_otp`**. Bearer **Enrollment** routes: **`2fa/setup`**, **`verify-setup`** (issues **`recovery_codes`** once), **`cancel-setup`**, **`disable`** (password and/or OTP/recovery for authenticator mode; password-only to disable email OTP). Managed MySQL should run migrations including **`EmailOtpChallenges`**, **`AddUserEmailVerificationAndSecondFactor`**, **`AddKiteFavoriteInstruments`**, **`AddKiteInstrumentsChartSettings`**, **`AddKiteInstrumentsChartZoomJson`**, **`AddKiteInstrumentsChartIntervalByInstrumentTokenJson`**, **`AddMlPriceDirectionPredictions`**, **`AddMlFavoriteEodReportSent`**, **`AddFavoriteMlUserAndPredictionSource`**, and **`AddBrokerAccountsAndHistoricalCandles`** ( **`BrokerAccounts`** + **`HistoricalCandles`**; Kite tokens move off **`Users`** ). Demo-only: **`POST /api/v1/auth/email-otp/*`**.

The SPA exposes **`/verify-email`**, **`/forgot-password`**, and **`/reset-password`** aligned with **`PublicWeb`** paths. The **`/profile`** page (**Account**, **Security** — email vs authenticator 2FA, **Broker**) is still the gate (`RequiresTwoFactor`, `RequiresBroker`). **`/security`** and **`/brokers`** redirects are unchanged.

Configuration merges `appsettings*.json` with **process environment variables**. Optional **`.env`** files are merged **only** when `ASPNETCORE_ENVIRONMENT=Development` (`DotEnvBootstrap`); **Production never reads `.env`**. **Integration tests** use `appsettings.IntegrationTesting.json` + an in-memory database and skip `.env`.

### 4. Run the web app

```bash
cd frontend
npm install
npm run dev
```

- Dev server uses `.env.development` (`VITE_*` variables).
- The client calls same-origin **`/api/v1`** by default; Vite proxies **`/api`** and **`/hubs`** to the API in dev. This avoids browser CORS preflight **`OPTIONS`** for authenticated requests.
- **Displayed instants** in the UI use the browser’s local timezone in **`DD/MM/YY HH.mm.ss.fff`** (24-hour clock, millisecond precision) via **`frontend/src/utils/formatLocalDateTime.ts`** (HTML **`datetime-local`** inputs keep `yyyy-MM-ddTHH:mm` as required).
- On **Kite instruments** at **`/instruments`**, the chart toolbar includes **Candles**: green/red OHLC candlesticks with a **time axis** along the bottom and **hover** OHLC/VOL + MA details; **Zoom** (+ / − / Reset) can narrow the view to as few as **one** recent bar or reset to the full downloaded series; **Full screen** uses the browser’s fullscreen API on the zoom + plot panel (favorites tiles and **Browse** detail chart) and keeps range caption, ML controls, and (**Browse**) symbol / LTP / favorites, chart toolbar, and ML bar in a **scrollable top-left** strip above the chart; visible-bar count is **saved per instrument** (server) and restored when you reopen charts; overlays **SMA 20** (amber), **EMA 9** (violet), **EMA 21** (sky), and an optional **custom-period EMA** (orange, period 2–500, toggle + number input; preference in **localStorage**) on line, bar, and candle plots (optional **Trend LR** — least-squares regression over visible closes — on **Candles** only; unrelated to toolbar **Trend analysis**, which multi-selects intervals and calls **`…/broker/kite/chart/historical-ohlc`** per selection for LR-on-close summaries over the same **Range** as the chart/favorites tile); indicator toggles are **local UI** only (except custom EMA prefs in **localStorage**). **Search Kite** runs the server file scan on **Enter** or button click whenever the query is non-empty (including when the capped preview already shows matches); **Browse** uses one **segment** per list (F&O vs MCX); **All favorites** merges **both** segment searches. **All favorites** chart tiles include the same **ML next-bar bias** control as **Browse** (calls `predictions/price-direction` per instrument; **history** for that bias is stored **per user** on the server). **Full predictions** (browser fullscreen) shows **one pie chart per model** returned by **`GET …/predictions/price-direction/models`** (server order; **correct** / **wrong** / **pending** counts combine **classic** and **LightGBM** history rows per `modelId`), plus **taller scrollable** history tables for each store (**filter** search above each table). The **Auto predictions** tab has the server **Auto ML for favorites** toggle, the same row **filter**, and requests up to **5000** merged classic + LightGBM rows from **`GET …/predictions/price-direction/automation-recent?take=`** (capped server-side). **Refresh chart** (next to zoom / fullscreen, or while a chart is still loading) and **↻ Predictions** reload OHLC and prediction history immediately. With Zerodha connected and a row selected on **Browse**, SignalR ticks update the **in-progress** bar for the chosen interval.

Production build:

```bash
cd frontend
npm run build
```

Production builds also default to same-origin **`/api`** / **`/hubs`**; set cross-origin env only as an explicit escape hatch.

## Docker Compose (full stack)

From the **repository root**:

```bash
docker compose up --build
```

| Service | Host URL / port | Notes |
|---------|-----------------|--------|
| **web** (SPA) | **http://localhost:8080** | nginx serves `frontend` production build; client calls same-origin **`/api`** and **`/hubs`**, which nginx proxies to the **api** service. |
| **api** | **http://localhost:5232** | Swagger: `/swagger`, health: `/health`. |
| **mysql** | **localhost:3307** → container `3306` | Change if **3307** is taken. API inside Compose still uses `mysql:3306`. |
| **redis** | **localhost:6379** | Reserved for future use. |

CORS in Compose still allows **`http://localhost:8080`** (Docker UI) and **`http://localhost:5173`** (optional local `npm run dev`) for direct API debugging, but the browser SPA normally uses same-origin proxy routes. **SignalR** (`/hubs/market`) uses WebSockets; ensure any **reverse proxy** in front of the API allows **upgrade** and long-lived connections for that path.

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

**DigitalOcean App Platform** (and similar reverse proxies): configure the API component **environment variables** at least: **`Jwt__Issuer`**, **`Jwt__Audience`**, **`Jwt__Key`** (UTF-8 secret ≥ 32 bytes), **`Cors__Origins__0`** (your SPA URL), **`Database__Provider=MySQL`**, **`ASPNETCORE_ENVIRONMENT=Production`**, and **`Database__Host`**, **`Database__Name`**, **`Database__Username`**, **`Database__Password`** (optional **`Database__Port`**, **`Database__SslMode=Required`** for managed MySQL). The API enables **`X-Forwarded-*`** headers so HTTPS termination at the edge works with **`UseHttpsRedirection`**. **Data Protection** (broker token and **TOTP secret** encryption): set **`DataProtection__KeyRingPath`** to a **persisted** directory (e.g. App Platform mounted volume) so keys survive redeploys. If that is unset in Production, the app uses an in-memory key ring (no filesystem warnings; encrypted payloads still become invalid after a process restart). **`GET /api/v1/broker/status`** validates Kite token decryption; rows that cannot be decrypted are removed and the response is **disconnected** until the user completes Kite OAuth again. **Do not commit** key directories.

**404 on production (SPA + API on App Platform)** usually means routing or client-side routing:

1. **API calls return 404** — the browser requests `https://<your-app>/api/v1/...`. If ingress sends `/api` traffic to the static site, or **strips** the `/api` prefix before the .NET service, Kestrel sees the wrong path (for example `/v1/...` instead of `/api/v1/...`) and returns 404. Fix in the app **ingress** (Settings → your app → **Ingress** or edit the app spec): add a rule with path prefix **`/api`** pointing at your **Web Service** (API) component and set **`preserve_path_prefix: true`**. Add a second API rule for **`/hubs`** with the same setting so SignalR stays same-origin too. List **`/api`** and **`/hubs`** before the catch‑all **`/`** rule that serves the **Static Site**. Do not set the frontend build to a separate API hostname unless you intentionally accept CORS preflight **`OPTIONS`**.
2. **Refreshing a deep link (e.g. `/profile`, `/instruments`) returns 404** — the static host has no file at that path. In the **Static Site** component, under **Custom Pages**, set **Catchall** to **`index.html`** (see [Manage static sites — Custom Pages](https://docs.digitalocean.com/products/app-platform/how-to/manage-static-sites/)). The web build also emits **`404.html`** (copy of `index.html`) for hosts that use a custom error page.

**`nginx: [emerg] host not found in upstream "api"`** at SPA boot — you deployed `frontend/Dockerfile` (the nginx image) as an App Platform **Service** instead of as a **Static Site**. The `api` hostname only resolves inside Docker Compose (where the API container is named `api`). On App Platform there is no such DNS name, so the proxy upstream cannot be parsed and the container exits. Two fixes:

1. **Preferred**: deploy the SPA as a **Static Site** per **`.do/app.yaml`** (`static_sites: trader-web`). The nginx image is *not* used in this path; App Platform's edge ingress routes **`/api`** and **`/hubs`** straight to the **`trader`** API service. If your live App spec currently lists the SPA under `services:`, switch it back to `static_sites:` and re-deploy.
2. If you really want to deploy the nginx image as a service (custom VM, single-container hosting, …), set the env var **`API_UPSTREAM`** to a URL that *is* reachable from the SPA container. Defaults to **`http://api:8080`** for Docker Compose; on App Platform private networking that would be the API component's `${trader.PRIVATE_URL}` (no trailing slash). The image now uses a runtime DNS lookup so it will not crash on boot, but requests still need a real upstream to succeed.

#### Zerodha Kite Connect

1. Create a Kite Connect app at [developers.kite.trade](https://developers.kite.trade/).
2. Set the **Redirect URL** in the developer console to exactly the same value as **`ZerodhaKite__RedirectUrl`** (e.g. dev API: `http://localhost:5232/api/v1/broker/kite/callback`). Mismatches cause OAuth failures.
3. Set **`ZerodhaKite__ApiKey`**, **`ZerodhaKite__ApiSecret`**, and **`ZerodhaKite__RedirectUrl`** as **environment variables** (Production/Staging) or in **`.env.development`** / **`.env.development.local`** when `ASPNETCORE_ENVIRONMENT=Development` (merged into configuration by `DotEnvBootstrap`). **Do not** put these values in committed **`appsettings*.json`**. Optionally override **`ZerodhaKite__PostLoginRedirectUrl`** if your SPA runs on a different origin than the default **`http://localhost:5173/profile#broker-connection`** (where users return after OAuth; should open **Profile → Broker connection**).
4. Apply EF migrations so `Users` matches the model (e.g. **`Migrate()`** on API startup for MySQL, **`dotnet build`** with **`RunEfMigrationsOnBuild=true`**, or `dotnet ef database update`).

Successful redirects from Kite include **`request_token`**; a **`status=success`** query parameter is **not** always present. The API treats the callback as failed only if **`status`** is sent and is **not** `success`.

**Split SPA and API (two public hostnames)** — e.g. static site at `https://trader-fe-vumpy.ondigitalocean.app` and API at `https://trader-be-7cdnc.ondigitalocean.app` ([Trader Console](https://trader-fe-vumpy.ondigitalocean.app/) · [API root](https://trader-be-7cdnc.ondigitalocean.app/)). This mode will produce browser CORS preflight **`OPTIONS`** for authenticated API calls because of the Bearer token header; prefer one public app hostname with **`/api`** and **`/hubs`** routed to the API when you want no UI `OPTIONS` calls:

| Where | Setting |
|--------|---------|
| **Frontend build** | **`VITE_FORCE_CROSS_ORIGIN_API=true`** and **`VITE_API_BASE_URL`** = `https://trader-be-7cdnc.ondigitalocean.app` (no trailing slash). Required only when the SPA origin must differ from the API origin. |
| **API env** | **`Cors__Origins__0`** = `https://trader-fe-vumpy.ondigitalocean.app` (exact SPA origin; no trailing slash). |
| **Kite developer console** | **Redirect URL** = `https://trader-be-7cdnc.ondigitalocean.app/api/v1/broker/kite/callback` — must be the **API** host, not the static site. |
| **API env** | **`ZerodhaKite__RedirectUrl`** = same URL as in Kite console. |
| **API env** | **`ZerodhaKite__PostLoginRedirectUrl`** = `https://trader-fe-vumpy.ondigitalocean.app/profile#broker-connection` (SPA URL where users land after OAuth; `#broker-connection` scrolls to the broker card). |

If **Redirect URL** is registered on the frontend hostname, Zerodha will call the wrong service and linking will fail.

The API exchanges the `request_token` at Kite’s token endpoint and stores **encrypted** access (and refresh when present) tokens using ASP.NET Core Data Protection. The login URL also sets Kite **`redirect_params`** with **`trader_oauth=<short id>`** so Zerodha echoes it on the callback if the main **`state`** query is missing. The OAuth **`state`** sent to Zerodha is a **short id** mapped in **server memory** to the HMAC-signed payload (`AddMemoryCache`); this avoids long URLs being dropped. **Split SPA/API**: the HttpOnly fallback cookie uses **`SameSite=None`** and **`Secure`** on HTTPS so credentialed XHR from the SPA origin can store a cookie for the API host (still secondary to **`state` / `trader_oauth`**). **Scaling to multiple API instances** requires **session affinity** or a **shared distributed cache** for that mapping (single-instance App Platform is fine). OAuth **`state`** signing for the stored payload still uses **`Jwt:Key`**. Persist **`DataProtection__KeyRingPath`** so stored broker tokens and 2FA secrets keep working across deploys. **Do not commit API secrets**; keep them in local env files or a secrets manager.

- **Instruments (F&O + MCX)**: with a valid Zerodha-linked session, `GET /api/v1/broker/kite/instruments/fno-commodities` streams Kite’s daily CSVs with up to **50** rows **per exchange** (NFO, BFO, MCX; `fnoTruncated` / `commoditiesTruncated` when more rows exist on Kite); the **F&amp;O** payload merges NFO+BFO and **caps the combined list at 50** rows. **`GET /api/v1/broker/kite/instruments/today-top-performers`** reloads that same capped universe server-side and ranks contracts by **`%`** gain vs **Kite’s prior-session close** (batched **`/quote/ohlc`** snapshots); **`items`** embed the instrument row plus **`lastPrice`**, **`previousClose`**, **`changePercent`**; query **`take`** clamps **1–30** (default **15** if omitted or ≤0). On **Browse**, the SPA requests **`take=30`**, shows **5** movers at first, and **Load more** adds **5** per click. **`GET /api/v1/broker/kite/instruments/search?q=…&segment=fno|mcx|spot|all`** streams the same CSVs server-side; **`all`** merges **fno**, **mcx**, and **spot** (deduped). **No row cap** — search returns **every** match in Kite's daily CSV (the 50-row limit is for the Browse-tab preview lists only): NFO+BFO merged for `fno`, MCX for `mcx`, NSE+BSE for **`spot`** — Kite cash **EQ**/**BE**/**BZ**, **INDEX** if used, and index rows whose **segment** contains **INDICES** per [Kite instruments](https://kite.trade/docs/connect/v3/market-data-and-instruments/). **`spot`** is NSE/BSE cash + indices only (not MCX commodities—use **`mcx`** or **`all`** for e.g. gold). Multi-word **`q`**: every token must appear in the row (e.g. **gold mini** matches **GOLDMINI**). **`scanTruncated`** is reserved for upstream truncation and is normally **false** for searches. **`GET /api/v1/broker/kite/historical-candles?instrumentToken=…&interval=…`** returns OHLCV from Kite’s historical API (`interval` codes: `1m`, `2m`, `3m`, `4m`, `5m`, `10m`, `15m`, `30m`, `1h`, `4h`, `1d`, `1w` — server builds **`4h`** from Kite **60minute** bars and **`1w`** from **day** bars in groups of seven sessions; optional `from` / `to` as ISO instants, UTC), plus per-candle **`sma20`**, **`ema9`**, **`ema21`**, **`srSupport`**, **`srResistance`** (nullable; 20-bar trailing min low / max high, same rule as the SPA labels). **`GET /api/v1/broker/kite/chart/historical-ohlc`** returns OHLCV only and **`GET /api/v1/broker/kite/chart/historical-overlays`** returns SMA/EMA/SR aligned to the same candle window (split payloads for parallelism; charts merge client-side); the server caches a composite result about **25s** per user/instrument/range so parallel calls avoid repeat Kite historical fetches within the TTL. **`GET /api/v1/broker/kite/chart/live-quote?exchange=…&tradingsymbol=…`** is a refreshed LTP/ohlc snapshot with about **5s** cache. The server pulls additional bars **before** the requested window (by interval) so SMA/EMA/support–resistance are warmed through the first visible bar. **`GET /api/v1/broker/kite/historical-candles`** remains the combined JSON payload. **Favorite instruments** (saved rows for charts/lists): **`GET /api/v1/broker/kite/favorites`** returns `{ items: [ … ] }` (same shape as instrument rows); **`POST /api/v1/broker/kite/favorites`** with a JSON body matching that row adds a favorite (idempotent if already saved); **`DELETE /api/v1/broker/kite/favorites?instrumentToken=…`** removes it. Favorites are stored in **`KiteFavoriteInstruments`** (MySQL) per user. **Locked for trading** uses the same JSON row shape with **`GET` / `POST` / `DELETE /api/v1/broker/kite/trading-locks`** ( **`DELETE`** query **`instrumentToken`** ); list table **`KiteTradingLockInstruments`**. **Chart toolbar** (interval, range preset, line / bar / candlestick; legacy stored **`graphType=trend`** is read as **`candlestick`** with **Trend LR** enabled in the SPA): **`GET /api/v1/broker/kite/instruments/chart-settings`** returns `{ interval, rangePreset, graphType, zoomByInstrumentToken, mlAutomationEnabled, mlAutomationInterval, mlAutomationPollIntervalMinutes, trendAnalysisIntervals }` (first three may be null until saved; **`mlAutomationEnabled`** is the per-user opt-in for **background** favorite ML; **`mlAutomationInterval`** / **`mlAutomationPollIntervalMinutes`** are optional per-user automation bar size and min **whole minutes** after the previous new pass **started** (stored as seconds in MySQL); **`zoomByInstrumentToken`** maps numeric Kite `instrument_token` strings to **visible bar count** for chart zoom, or null/omitted when empty); **`PUT /api/v1/broker/kite/instruments/chart-settings`** with `{ interval, rangePreset, graphType }` (all three required) and optional **`mlAutomationEnabled`** (omit to leave that flag unchanged) and optional **`trendAnalysisIntervals`** (string array of chart codes; omit to leave stored trend checkboxes unchanged) persists those fields on **`Users`** without clearing saved zoom. **`PUT /api/v1/broker/kite/instruments/favorite-ml-automation`** with `{ enabled }` (required) and optional **`interval`** (empty string clears the per-user automation bar interval so **`FavoriteMlAutomation:PredictionIntervalOverride`** / chart settings apply) and **`pollIntervalMinutes`** (omit to leave unchanged; **0** clears per-user throttle; **1–1440** sets min whole minutes after the previous new pass **started** before the next pass; pending rows still resolve every global cycle). **`PUT /api/v1/broker/kite/instruments/chart-zoom`** with `{ instrumentToken, visibleBars }` (`visibleBars` null removes zoom for that token) persists zoom per instrument. Requires `Authorization: Bearer …`.
- **ML prediction (experimental)**: **`GET /api/v1/predictions/price-direction/models`** returns **`defaultModelId`** and **`models`** (`id`, `description`). **`GET /api/v1/predictions/price-direction?instrumentToken=…&interval=…`** supports optional **`model=<id>`** (Bearer; same `interval` codes); omit **`model`** to use **`PriceDirectionPrediction:DefaultModelId`** (**`mlnet-sdca-logistic-v1`** unless overridden in **`appsettings`** / **`PriceDirectionPrediction__DefaultModelId`**). Built-ins today: **ML.NET LightGBM** (**`mlnet-lightgbm-triple-barrier-v1`**; on-the-fly training on OHLC + backend SR/EMA with a **candle-bar** triple-barrier label), **ML.NET SDCA** logistic regression (**`mlnet-sdca-logistic-v1`**; short-horizon returns + SMA gap on closes), and a **momentum** baseline (**`momentum-close-v1`**). Add new engines in Infrastructure and register them in **`AddInfrastructure`**. Response adds optional **`predictionId`**, **`refBarTime`**, **`refClose`**, **`predictedAt`** when the run is **persisted**, and **`predictionStorage`** as **`classicPriceDirection`** or **`lightgbmTripleBarrier`** so clients know which MySQL table holds the row. **`GET /api/v1/predictions/price-direction/history?instrumentToken=…&interval=…&take=…`** returns classic-table rows (newest first, cap 2000). **`GET /api/v1/predictions/price-direction/lightgbm-triple-barrier/history?…`** returns LightGBM-only rows from **`MlLightGbmTripleBarrierPredictions`**. **`GET /api/v1/predictions/price-direction/automation-recent?take=…`** merges automation **`Source=automation`** rows from **both** tables (sorted by **`predictedAt`**; each row exposes **`engineModelId`** plus scoring **`modelId`**; **`take`** is clamped up to **5000** internally for merge headroom). Optional **`fromUtc`** and **`toUtcExclusive`** (ISO instants, UTC) filter on **`PredictedAtUtc`** in **`[fromUtc, toUtcExclusive)`** (same half-open window as the manual automation email; span ≤ **93** days; mixed omit/supply rejected). Omit both for legacy behavior (recent rows without a date filter). The **Auto predictions** SPA defaults the browser-local range to **today 00:00 → now** and passes these query params when the range is valid. **`POST /api/v1/predictions/price-direction/automation-report-email`** (Bearer; optional JSON **`{ fromUtc, toUtcExclusive }`** as ISO datetimes in **UTC**; rows match **`PredictedAtUtc`** in **[fromUtc, toUtcExclusive)**; omit **both** for **today** as a full calendar day in **`FavoriteMlAutomation:ReportTimeZoneId`**; span ≤ **93** days; mixed null/non-null rejected; requires **SMTP** + profile **email**) sends automation-only rows using **multipart email**: **HTML** parts with inline **PNG** outcome pies (**`cid:`**-linked images; combined + per engine) plus a **CSV** attachment; unlike the nightly EOD job this is **not** deduped in **`MlFavoriteEodReportsSent`**. SPA: **Email automation report** on **Auto predictions** (browser **`datetime-local`** range → **`fromUtc`** / **`toUtcExclusive`** POST body). **`PATCH /api/v1/predictions/price-direction/{id}/resolve`** with JSON **`{ "nextBarTime", "nextClose" }`** resolves a pending row in **either** table by id. Each table is capped per user (25k); oldest prune on insert for that store. The SPA shows **two** history panels (classic vs LightGBM) and resolves both against the chart series; **`localStorage`** fallback applies only when the classic history GET fails. Not investment advice; ML.NET path uses heuristic fallback if training fails or output is near 50%. **Optional automation:** when **`FavoriteMlAutomation:Enabled`** is **true** (off in default **`appsettings.json`**), a **background service** runs on **`PollIntervalSeconds`** when set (**&gt; 0**, clamped **15–3600**), otherwise every **`PollIntervalMinutes`** (**1–120**). Root **`appsettings.json`** includes **`PollIntervalSeconds`: 60** as a template; omit it (class default **0**) to keep minute-only polling. For each user who has **favorite** instruments **and** **`Users.FavoriteMlAutomationEnabled`**, it **resolves** pending rows when the next candle exists, then for each favorite + latest ref bar invokes **every** registered engine (**`GET …/predictions/price-direction/models`**), persisting separately by store (LightGBM vs classic) with **`EngineModelId`** for dedupe. Optional **`PredictionModelId`**: comma-separated engine ids **subset** only; omit, null, or empty = run **all** registered engines. **`PredictionIntervalOverride`** (default **`1m`**) applies when the user has not set a saved automation interval; leave **empty** to use saved chart settings (toolbar + per-favorite interval map). The SPA can persist a per-user automation interval and optional poll throttle via **`PUT …/favorite-ml-automation`**. **`BestOfThreeEnabled`** (default **true** in template **`appsettings.json`**) runs **three** sliding-window inferences on the same latest bar (drop 0/1/2 oldest candles) and stores the **majority** direction; the row’s **`detail`** begins with **`[b3 u=… d=… n=… v=… m=…]`**; nightly/manual automation emails add an aggregate **up/down/neutral** vote pie when any row carries that tag; CSV adds **`b3*`** columns. When there are fewer than **`MinCandlesRequired + 2`** candles, automation falls back to a **single** inference. New rows store **`Source=automation`**. When **`QuietHoursEnabled`** is **true** (default in **`FavoriteMlAutomation`**), the background job skips only **new** scheduled ML predictions for favorites during the local nightly window (**`QuietHoursStart/End*`** in **`ReportTimeZoneId`**; **`appsettings.json`** template stops **23:25** and resumes **08:00** IST with **`Asia/Kolkata`**); **`PauseAutomationOnWeekends`** (default **true**) skips **new** predictions all day on **Saturday/Sunday** in that TZ. **pending** automation rows **still resolve** when the next bar is available so correct/wrong can update overnight. **`LiveCandles`** and interactive API predictions are unaffected. Users **without a valid Kite session** are skipped. If **`Smtp:IsEnabled`** is **true**, once per **local** day (**`ReportTimeZoneId`**, default **`Asia/Kolkata`**) starting at **`ReportLocalHour`/`ReportLocalMinute`** (default **23:30 IST** via **`Asia/Kolkata`**, two-hour send window), the API emails that user **multipart/HTML** (inline **PNG** combined outcome chart via **`cid:`**) and a **CSV** listing **every** prediction across engines (**`engineModelId`**, **`modelId`**, outcomes) whose **`PredictedAtUtc`** falls on that calendar day (**`MlFavoriteEodReportsSent`** prevents duplicates). See **`FavoriteMlAutomation`** and **`PriceDirectionPrediction`** in **`appsettings.json`** and **`.env.example`**.

### Web (`frontend/`)

SPA paths: **`/instruments?tab=favorites`** (or **`?tab=fav`**, **`?fav=1`**, **`?fav=true`**) opens **All favorites** (toolbar **Trend analysis** = multi-select of any chart intervals (`1m`–`1w`) for parallel past-window LR summaries via **`…/broker/kite/chart/historical-ohlc`**; selections persist to **`GET/PUT …/chart-settings`** `trendAnalysisIntervals` with **localStorage** as a fallback when nothing is saved yet); **Interval** still sets the single bar size on **all** charts and clears per-symbol overrides; each favorite tile shows **Multi-interval trend** under the chart with a loading state while OHLC fetches); **`?tab=locked`** (or **`?tab=trading-locks`**) opens **Locked for trading** (same grid idea as favorites, persisted under **`/broker/kite/trading-locks`**); **`?tab=automation`** (**`/instruments/automation`**) opens **Auto predictions** (server automation toggle, per-user **bar interval** / **min minutes after previous new pass started**, merged log; **direction vote** pie summarizing predicted **up** / **down** / **neutral** for the filtered automation rows, then per-engine **pie charts** for correct/wrong/pending (each tile also shows the engine’s **prediction accuracy %** = correct / (correct + wrong) over resolved rows in the current filter), **Direction** / **outcome** / **interval** / **engine** filter toggles (plus search); the recent-rows table renders **Category** (Browse-tab grouping resolved from the row's exchange — **F&O** for NFO/BFO, **Commodities** for MCX, **Spot** for NSE/BSE equity / indices) and **Conf %** (engine confidence per row, 0–100); the row search box matches against the category label too; **Email automation report** accepts a configurable **datetime range** (browser local → UTC **`fromUtc`** / **`toUtcExclusive`**) instead of relying on report-tz “today” only; the merged **recent** list uses the same range via **`GET …/automation-recent`** when valid; search—table and pies use the same filtered rows); **`/instruments/fav`** redirects to **`?tab=favorites`**; **`/instruments/locked`** redirects to **`?tab=locked`**. **`/instruments`** without **`tab`** is **Browse**. Switching tabs updates the query with **`replace`**. On **Browse**, **Today's top performers** ranks capped preview contracts by OHLC-derived % vs the prior session (fetches up to **30** server-side, **5** rows visible at a time with **Load more**) and supports opening the chart from a row.

| File | Purpose |
|------|---------|
| `.env.development` | Optional `VITE_DEV_SERVER_PORT`, `VITE_API_PROXY_TARGET`; same-origin **`/api`** / **`/hubs`** is the default. |
| `.env.production` | Same-origin **`/api`** / **`/hubs`** is the default. Use **`VITE_FORCE_CROSS_ORIGIN_API=true`** + **`VITE_API_BASE_URL`** only for a deliberate split-host deployment. |

See `frontend/.env.example`. Only variables prefixed with `VITE_` are exposed to the browser.

The SPA uses **React Bootstrap** (components) and **Bootstrap 5** (CSS). `index.html` defaults **`data-bs-theme="dark"`**; on load **`main.tsx`** applies the saved **Profile → Appearance** choice (light / dark / system) from **`localStorage`** (`**trader-theme-preference**`) so the whole app, including **login**, matches the user’s mode.

Example layout for production: see **[Deploy to DigitalOcean App Platform](#deploy-to-digitalocean-app-platform)** and **`.do/app.yaml`**.

## Deploy to DigitalOcean App Platform

High-level path: **Managed MySQL** + **one App** from this monorepo with a **Web Service** (API) and a **Static Site** (frontend), plus **ingress** so **`/api` → API**, **`/hubs` → API**, and **`/` → static**. TLS is at the edge; the .NET container listens on HTTP **8080** internally (match **`http_port`** in the spec).

### 1. Database

- Create a **Managed MySQL** cluster (same region as the app helps latency).
- Note **host**, **port** (often **25060**), **database**, **user**, **password**; use TLS (**`Database__SslMode=Required`** — see [Configuration](#configuration)).

### 2. Source control

- Push this repo to **GitHub** (or GitLab). Edit **`.do/app.yaml`**: set **`github.repo`**, **`branch`**, **`region`**, and instance sizes as needed.

### 3. Create / update the app

- **Apps → Create** → GitHub → pick repo. Paste or import the spec from **`.do/app.yaml`**, or after the first wizard pass use **Settings → App Spec** / **`doctl apps update --spec .do/app.yaml`**.
- Ensure **ingress** matches the file: **`/api`** and **`/hubs`** first with **`preserve_path_prefix: true`** on the **Web Service** named **`trader`** (or rename consistently), then **`/`** to static **`trader-web`**.

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
- Do **not** set **`VITE_API_BASE_URL`** for the normal one-host deployment. The SPA calls same-origin **`/api`** and **`/hubs`**, which avoids CORS preflight **`OPTIONS`**. Only for a required split-host deployment, set **`VITE_FORCE_CROSS_ORIGIN_API=true`** and **`VITE_API_BASE_URL`** to the API origin.

### 6. Database schema

- Run once from your machine (or a CI job) against production:

  ```bash
  cd backend
  dotnet ef database update --project src/Trader.Infrastructure --startup-project src/Trader.Api
  ```

  Use the same **`Database__*`** (or alias) variables as production so migrations hit the managed DB. **Image builds** use **`/p:RunEfMigrationsOnBuild=false`**; **production** still applies migrations **on API startup** unless you set **`Database__ApplyMigrationsOnStartup=false`**.

**Troubleshooting — `Unknown column '...KiteInstrumentsChartZoomJson'` or **`...KiteInstrumentsChartIntervalByInstrumentTokenJson`** (login/register/forgot-password 500):** The model includes the matching migration, but the live **`Users`** table in the catalog the API uses (logs show **`TABLE_SCHEMA='defaultdb'`**) never got the column while **`__EFMigrationsHistory`** already lists that migration—so **`Migrate()`** reports *already up to date* and does nothing. Repair on that database (DigitalOcean MySQL console or any client), idempotent on MySQL 8+:

```sql
ALTER TABLE `Users`
  ADD COLUMN IF NOT EXISTS `KiteInstrumentsChartZoomJson` longtext NULL;
ALTER TABLE `Users`
  ADD COLUMN IF NOT EXISTS `KiteInstrumentsChartIntervalByInstrumentTokenJson` longtext NULL;
```

On older MySQL, run a plain **`ADD COLUMN`** once, or use **`SHOW COLUMNS FROM Users LIKE 'KiteInstrumentsChartZoomJson';`** (and the interval column name) to confirm before adding. Ensure you are connected to the **same** schema as **`Database__Name`** (managed clusters often use **`defaultdb`**).

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
