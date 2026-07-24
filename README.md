# Trader

This repository contains **two deployable projects**: **`backend/`** (.NET 8 API) and **`frontend/`** (Vite + React). They can stay in one Git repo or be pushed to **separate remotes** (copy each folder into its own repository root if you split).

Monorepo overview: REST API with **JWT**, **EF Core** + **MySQL**, and a **React (TypeScript)** UI for instruments, scalper, trades, broker onboarding, **email verification**, **password reset links**, and **second-factor sign-in** (authenticator **TOTP** or **email OTP** after password).

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
| `logging/` | Optional Elasticsearch + Kibana Compose stack (`logging/docker-compose.yml`) |

**Conventions:** root **`.editorconfig`** keeps C# / TS / YAML formatting aligned; **`AGENTS.md`** is a short map for humans and AI tools. **`backend/global.json`** pins the .NET **8** SDK band with **`rollForward`: `latestFeature`** (stay on **8.x** for App Platform — do not use **`latestMajor`**, or the buildpack may install runtime **10** while the app still requires framework **8.0**). Backend layering and SOLID expectations are summarized in **`.cursor/rules/core-principles.mdc`** (see “SOLID in this repo” there).

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

Set **`DATABASE_URL`** in `backend/src/Trader.Api/.env.development` (see **`.env.example`**), e.g. **`mysql://root:YOUR_PASSWORD@localhost:3306/trader`**. Put real passwords and broker keys in **`.env.development.local`** (gitignored). **Docker Compose** maps container MySQL to **`localhost:3307`** on the host (see `docker-compose.yml`) so it does not fight for **3306** with another MySQL install; from **`dotnet run`** on the host against that Compose database use port **3307** in **`DATABASE_URL`**. Services inside Compose (`api` → `mysql`) use **`DATABASE_URL=mysql://trader:trader@mysql:3306/trader`** internally.

With **`Database:Provider=MySQL`** (and not **IntegrationTesting**), the API runs **`Migrate()`** on startup when **`Database:ApplyMigrationsOnStartup`** is **true** (default in `appsettings.json`). In **Development** it also **creates the database** if missing. **Production** and **Docker** typically use an existing catalog (e.g. managed MySQL) and only apply pending migrations. Set **`Database__ApplyMigrationsOnStartup=false`** to disable automatic startup migrations. You can still apply migrations manually (`dotnet ef`, below).

When you **`dotnet build`** the API from `backend/` in **Debug**, **`dotnet ef database update`** runs after the API project build by default (**`RunEfMigrationsOnBuild`** is **true**), which requires the **`dotnet-ef`** global tool and **reachable MySQL** (same **`DATABASE_URL`** / `.env` as **`dotnet run`**). For **Release** builds (including **`dotnet publish`** on hosts like DigitalOcean App Platform that do not install **`dotnet-ef`**), **`RunEfMigrationsOnBuild`** defaults to **false** so publish succeeds; rely on **`Migrate()`** on API startup (or run **`dotnet ef database update`** from a machine with the tool) unless you pass **`-p:RunEfMigrationsOnBuild=true`**. Use **`-p:RunEfMigrationsOnBuild=false`** when the database is unavailable during compile. **`dotnet test`** references the API with **`RunEfMigrationsOnBuild=false`**. **Docker** **`dotnet publish`** can still pass **`/p:RunEfMigrationsOnBuild=false`** explicitly.

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

**Authentication:** **`POST /api/v1/auth/register`** responds **`{ "email_verification_required": true }`** and emails a verification link (**`Smtp__*`**, **`PublicWeb__FrontendBaseUrl`** — see **`.env.example`**). **`POST /api/v1/auth/verify-email`** **`{ "token" }`** returns a JWT. **`POST /api/v1/auth/login`** returns **`{ "requires_email_verification": true }`** when the password is correct but email is not confirmed yet. Once verified, normal login returns a JWT or **`{ "requires_2fa": true, "temp_token": "…", "second_factor": "authenticator" | "email_otp" }`** when a second factor is enabled; complete with **`POST /api/v1/2fa/verify-login`**. Login is rate-limited per client IP (**8 attempts per minute**, then HTTP **429**) and every successful login persists IP audit metadata (remote IP, forwarded IP headers, and user-agent) in **`UserLoginAudits`**. Use **`POST /api/v1/auth/resend-login-otp`** for **`email_otp`**. **`GET /api/v1/auth/me`** includes **`email_verified`**. **Password reset:** **`POST /api/v1/auth/forgot-password`** `{ "email" }` emails a **6-digit code** (when the account exists); **`POST /api/v1/auth/reset-password`** `{ "email", "otp", "new_password" }` sets the new password. Legacy link reset via **`token`** + **`new_password`** on **`/reset-password`** is still supported. **`POST /api/v1/2fa/enable-email-sign-in`** (Bearer) turns on email codes; **`GET /api/v1/2fa/status`** includes **`second_factor_method`**.
**Verify-login `otp`** is a TOTP or recovery code when **`second_factor`** is **`authenticator`**, and the emailed six-digit code when **`email_otp`**. Bearer **Enrollment** routes: **`2fa/setup`**, **`verify-setup`** (issues **`recovery_codes`** once), **`cancel-setup`**, **`disable`** (password and/or OTP/recovery for authenticator mode; password-only to disable email OTP). Managed MySQL should run migrations including **`EmailOtpChallenges`**, **`AddEmailOtpChallengePurpose`**, **`AddUserEmailVerificationAndSecondFactor`**, **`AddKiteFavoriteInstruments`**, **`AddKiteInstrumentsChartSettings`**, **`AddKiteInstrumentsChartZoomJson`**, **`AddKiteInstrumentsChartIntervalByInstrumentTokenJson`**, **`AddMlPriceDirectionPredictions`**, **`AddMlFavoriteEodReportSent`**, **`AddFavoriteMlUserAndPredictionSource`**, **`AddFavoriteMlAutomationMinSecondsAfterBarOpen`**, **`AddDemoPaperBuyLegs`**, **`AddDemoPaperTradeLogs`**, **`AddUserLoginAudits`**, and **`AddBrokerAccountsAndHistoricalCandles`** ( **`BrokerAccounts`** + **`HistoricalCandles`**; Kite tokens move off **`Users`** ). Demo-only: **`POST /api/v1/auth/email-otp/*`**.

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
- ML prediction and auto-prediction flows are removed from the current product surface.
- On **Kite instruments** at **`/instruments`**, **Candles** (and **line/bar** modes) plot with **TradingView Lightweight Charts**: **price** (right gutter) + **time** scales are visible, with **wheel + drag** pan/zoom enabled on the time axis (including pinch / axis-drag scaling). Charts **open on the newest ~24 bars** (**`CHART_DEFAULT_VISIBLE_BARS`** in **`frontend/src/constants/chartLayout.ts`**); dragging toward the oldest loaded candle can **fetch older OHLC** (same **`from`** / **`to`** window span as your current payload, clipped at the backend’s **`from`**) merged into memory. **`crosshair`**, **tooltip labels**, and **`formatLocalDateTime`** reads still reinforce price/time; **next-bar ML** history renders **small directional cues** above candles (**one horizontal row of ↑ / ↓ / ◇ glyphs per model, plus a shape when all models agree**) and, on crosshair hover, shows **compact green / red / yellow badges per prediction** plus **numeric values for every enabled overlay** (SMA, EMAs, SR, Trend LR—same hues as the chart legend); **volume** stays under OHLC when enabled. The chart toolbar has **refresh** and **fullscreen** only (fractional **`PUT …/broker/kite/instruments/chart-zoom`** is **not called** by this SPA anymore—the column/API may remain for legacy data). **Full screen** uses the browser’s fullscreen API on the **chart panel** (favorites tiles, **Browse** detail chart, **Manual trade** scalper chart) and keeps range caption, ML controls, and (**Browse**) symbol / LTP / favorites, chart toolbar, and ML bar in a **scrollable top-left** strip above the chart (**Manual trade** fullscreen uses the same toolbar **without** the **Range** preset row, and still shows the range caption plus **refresh/fullscreen**); overlays **SMA 20** (amber), **EMA 9** (violet), **EMA 21** (sky), and an optional **custom-period EMA** (orange, period 2–500, toggle + number input; preference in **localStorage**) on line, bar, and candle plots (optional **Trend LR** — least-squares regression over visible closes — on **Candles** only; unrelated to toolbar **Trend analysis**, which multi-selects intervals and calls **`…/broker/kite/chart/historical-ohlc`** per selection for LR-on-close summaries over the same **Range** as the chart/favorites tile); indicator toggles are **local UI** only (except custom EMA prefs in **localStorage**). **`GET …/demo-paper-positions`** includes **`lastBuyPrice`** (latest BUY fill when **`openContracts` > 0); charts draw an **amber dashed** horizontal **Last buy** guide distinct from cyan **LTP**. **`POST …/demo-paper-trade`** **`contracts`** = **exchange lots** (cash ≈ **lots × Kite lot_size × LTP**); the API **re-reads lot_size** from Kite **`/instruments/{exchange}`** CSV on each fill and **syncs** the saved lock row when Kite differs. **Demo paper** BUYs on **locked** instruments draw **lime dashed** verticals on the candle that contains each buy; **sell** removes markers in **FIFO** lot order (same **browse**, **favorites**, and **locked-grid** charts). **Manual trade** scalper ( **`/instruments` → Manual trade** ) hides the toolbar **Range** row, loads **three calendar days** (**`last3d`**), merges live ticks (~**60s** refresh cadence as elsewhere), and follows the **same ~24‑bar opening viewport plus left‑edge prefetch** pattern as Browse and favorite tiles—**`/scalper`** minute ranges use identical chart UX. **Manual trade** also lists **demo paper** fills (`GET …/demo-paper-trades`) under the lock grid. **Search Kite** runs the server file scan on **Enter** or button click whenever the query is non-empty (including when the capped preview already shows matches); **Browse** uses one **segment** per list (F&O vs MCX); **All favorites** merges **both** segment searches. **All favorites** chart tiles include the same **ML next-bar bias** control as **Browse** (calls `predictions/price-direction` per instrument; on-chart ribbons use **Font Awesome** **green/red** arrows for up/down directions; **history** for that bias is stored **per user** on the server). **Full predictions** (browser fullscreen; **outcome pie grids minimised by default** — use **Expand pies**) shows **one pie chart per model** returned by **`GET …/predictions/price-direction/models`** (server order; **correct** / **wrong** / **pending** counts combine **classic** and **LightGBM** history rows per `modelId`), plus **taller scrollable** history tables for each store (**filter** search above each table). The **Auto predictions** tab has the server **Auto ML for favorites** toggle, the same row **filter**, and requests up to **5000** merged classic + LightGBM rows from **`GET …/predictions/price-direction/automation-recent?take=`** (capped server-side). **Refresh chart** (toolbar next to **fullscreen**, or while a chart is still loading) and **↻ Predictions** reload OHLC and prediction history immediately. With Zerodha connected and a row selected on **Browse**, SignalR ticks update the **in-progress** bar for the chosen interval.

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

### Optional logging stack (Elasticsearch + Kibana)

Serilog is already used by **`Trader.Api`**. In **Development**, logs are also shipped to Elasticsearch (`logs-trader-api` data stream) for Kibana. Start the stack, then run the API:

```bash
cd logging
docker compose up -d
```

| Service | Host URL |
|---------|----------|
| **elasticsearch** | **http://localhost:9200** |
| **kibana** | **http://localhost:5601** → **Discover** (create a data view on `logs-trader-api*` or `logs-*`) |

Security is off (`xpack.security.enabled=false`) for local use only. Production: leave ES sink off by default, or set **`Serilog__Elasticsearch__Enabled=true`** and **`Serilog__Elasticsearch__Nodes__0`**. Package: **`Elastic.Serilog.Sinks`**.

#### Heroku logging UI (`kibana` app)

Local Elastic Kibana **8.x** cannot talk to the free **Bonsai** Heroku add-on (OpenSearch-compatible). This repo ships **OpenSearch Dashboards** for that app under **`logging/heroku/`**.

```bash
# one-time
heroku addons:create bonsai:sandbox -a kibana
heroku stack:set container -a kibana
heroku config:set ELASTICSEARCH_HOSTS="$(heroku config:get BONSAI_URL -a kibana)" -a kibana

cd logging/heroku
heroku container:login
# Windows: classic builder avoids Heroku "unsupported" OCI push errors
set DOCKER_BUILDKIT=0
docker build --platform linux/amd64 -t registry.heroku.com/kibana/web .
docker push registry.heroku.com/kibana/web
# if push says unsupported:
# docker buildx build --platform linux/amd64 --provenance=false --sbom=false --output type=image,name=registry.heroku.com/kibana/web,push=true,oci-mediatypes=false .
heroku container:release web -a kibana
heroku ps:type standard-2x -a kibana
heroku ps:scale web=1 -a kibana
heroku open -a kibana
```

Live URL: **https://kibana-7dcb7cda65ae.herokuapp.com/** (Standard-2X). Sign in at **`/__auth/login`** with Heroku config **`DASHBOARDS_BASIC_AUTH_USER`** / **`DASHBOARDS_BASIC_AUTH_PASSWORD`** (not OpenSearch docs `admin`/`admin`). Example vars: **`logging/heroku/.env.example`**. Point the API Serilog sink at **`BONSAI_URL`** via **`Serilog__Elasticsearch__*`** (see **`backend/src/Trader.Api/.env.example`**) if you want production logs in the same cluster.

## Configuration

### API (`backend/src/Trader.Api`)

| Mechanism | Notes |
|-----------|--------|
| `appsettings.json` | Structure and non-secret defaults (many values empty by design) |
| `appsettings.Development.json` | Development logging |
| `.env` / `.env.development` / `.env.local` | **Development only.** Merged by `DotEnvBootstrap` when `ASPNETCORE_ENVIRONMENT=Development`. Set **`DATABASE_URL`** (and JWT/CORS) as in **`.env.example`**; secrets in **`.env.development.local`**. **Production ignores these files** — use platform env vars or `appsettings.Production.json` (non-secrets only). |
| `.env.production` (committed) | **Template / documentation only** for humans; the API does **not** load it at runtime in Production. |
| Environment variables | Override files. **`DATABASE_URL`** is preferred; alternatives include **`Database__ConnectionString`**, discrete **`Database__*`**, or **`MYSQL_*`** / **`DB_*`** aliases (see `MySqlConnectionStringResolver`). |

See `backend/src/Trader.Api/.env.example`. Local overrides: **`.env.local`** / **`.env.development.local`** (gitignored). **Production** uses only **environment variables** and appsettings — **`.env` / `.env.production` are never loaded** by the host when `ASPNETCORE_ENVIRONMENT` is not `Development`. Blank values in merged `.env` lines are ignored.

Required for a real MySQL run: **`Database:Provider=MySQL`** and **`DATABASE_URL`** (`mysql://user:pass@host:port/db?ssl-mode=Required`) — preferred on App Platform and Docker. Alternatives: **`Database__ConnectionString`**, **`ConnectionStrings__Default`**, or discrete **`Database__Host`** / **`Name`** / **`Username`** / **`Password`** (see `MySqlConnectionStringResolver`). You still need **JWT** and **CORS**.

**DigitalOcean Managed MySQL** uses port **25060** and TLS — include **`?ssl-mode=Required`** in **`DATABASE_URL`** (see **Connection details** in the control panel).

**DigitalOcean App Platform** (and similar reverse proxies): configure the API component **environment variables** at least: **`DATABASE_URL`** (encrypted), **`Jwt__Issuer`**, **`Jwt__Audience`**, **`Jwt__Key`** (UTF-8 secret ≥ 32 bytes), **`Cors__Origins__0`** (your SPA URL), **`Database__Provider=MySQL`**, **`ASPNETCORE_ENVIRONMENT=Production`**. Discrete **`Database__Host`** / **`Database__Name`** / … fields still work if you prefer them over **`DATABASE_URL`**. The API enables **`X-Forwarded-*`** headers so HTTPS termination at the edge works with **`UseHttpsRedirection`**. **Data Protection** (broker token and **TOTP secret** encryption): set **`DataProtection__KeyRingPath`** to a **persisted** directory (e.g. App Platform mounted volume) so keys survive redeploys. If that is unset in Production, the app uses an in-memory key ring (no filesystem warnings; encrypted payloads still become invalid after a process restart). **`GET /api/v1/broker/status`** validates stored broker token decryption; rows that cannot be decrypted are removed and the response stays disconnected until the user reconnects that broker. **Do not commit** key directories.

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

- **Instruments (F&O + MCX)**: with a valid Zerodha-linked session, `GET /api/v1/broker/kite/instruments/fno-commodities` streams each exchange CSV up to **4000** rows **per exchange** (NFO, BFO, MCX), sorts by **nearest `expiry`** (derivatives first by parsed date), then returns **50** displayed rows (**merged NFO+BFO** capped after sort; **`fnoTruncated`** / **`commoditiesTruncated`** when the exchange file continues past that buffer or when more than **50** rows remain after sorting — use **`GET …/instruments/search`** for the full file). **`GET /api/v1/broker/kite/instruments/today-top-performers`** reloads that same capped universe server-side and ranks contracts by **`%`** gain vs **Kite’s prior-session close** (batched **`/quote/ohlc`** snapshots); **`items`** embed the instrument row plus **`lastPrice`**, **`previousClose`**, **`changePercent`**; query **`take`** clamps **1–30** (default **15** if omitted or ≤0). On **Browse**, the SPA requests **`take=30`**, shows **5** movers at first, and **Load more** adds **5** per click. **`GET /api/v1/broker/kite/instruments/search?q=…&segment=fno|mcx|spot|all`** streams the same CSVs server-side; **`all`** merges **fno**, **mcx**, and **spot** (deduped). **No row cap** — search returns **every** match in Kite's daily CSV (the 50-row limit is for the Browse-tab preview lists only): NFO+BFO merged for `fno`, MCX for `mcx`, NSE+BSE for **`spot`** — Kite cash **EQ**/**BE**/**BZ**, **INDEX** if used, and index rows whose **segment** contains **INDICES** per [Kite instruments](https://kite.trade/docs/connect/v3/market-data-and-instruments/). **`spot`** is NSE/BSE cash + indices only (not MCX commodities—use **`mcx`** or **`all`** for e.g. gold). Multi-word **`q`**: whitespace separates AND phrases; within each phrase, **letter runs and digit runs** are separate tokens (e.g. **`nifty12may`** matches symbols containing **`nifty`**, **`12`**, and **`may`**). **`scanTruncated`** is reserved for upstream truncation and is normally **false** for searches. **`GET /api/v1/broker/kite/historical-candles?instrumentToken=…&interval=…`** returns OHLCV from Kite’s historical API (`interval` codes: `1m`, `2m`, `3m`, `4m`, `5m`, `10m`, `15m`, `30m`, `1h`, `4h`, `1d`, `1w` — server builds **`4h`** from Kite **60minute** bars and **`1w`** from **day** bars in groups of seven sessions; optional `from` / `to` as ISO instants, UTC), plus per-candle **`sma20`**, **`ema9`**, **`ema21`**, **`srSupport`**, **`srResistance`** (nullable; 20-bar trailing min low / max high, same rule as the SPA labels). **`GET /api/v1/broker/kite/chart/historical-ohlc`** returns OHLCV only and **`GET /api/v1/broker/kite/chart/historical-overlays`** returns SMA/EMA/SR aligned to the same candle window (split payloads for parallelism; charts merge client-side); the server caches a composite result about **25s** per user/instrument/range so parallel calls avoid repeat Kite historical fetches within the TTL. **`GET /api/v1/broker/kite/chart/live-quote?exchange=…&tradingsymbol=…`** is a refreshed LTP/ohlc snapshot with about **5s** cache. The server pulls additional bars **before** the requested window (by interval) so SMA/EMA/support–resistance are warmed through the first visible bar. **`GET /api/v1/broker/kite/historical-candles`** remains the combined JSON payload. **Favorite instruments** (saved rows for charts/lists): **`GET /api/v1/broker/kite/favorites`** returns `{ items: [ … ] }` (same shape as instrument rows); **`POST /api/v1/broker/kite/favorites`** with a JSON body matching that row adds a favorite (idempotent if already saved); **`DELETE /api/v1/broker/kite/favorites?instrumentToken=…`** removes it. Favorites are stored in **`KiteFavoriteInstruments`** (MySQL) per user. **Locked for trading** uses the same JSON row shape with **`GET` / `POST` / `DELETE /api/v1/broker/kite/trading-locks`** ( **`DELETE`** query **`instrumentToken`** ); list table **`KiteTradingLockInstruments`**. **Chart toolbar** (interval, range preset, line / bar / candlestick; legacy stored **`graphType=trend`** is read as **`candlestick`** with **Trend LR** enabled in the SPA): **`GET /api/v1/broker/kite/instruments/chart-settings`** returns `{ interval, rangePreset, graphType, zoomByInstrumentToken, mlAutomationEnabled, mlAutomationInterval, mlAutomationPollIntervalMinutes, mlAutomationMinSecondsAfterBarOpen, trendAnalysisIntervals, demoAutoTradeEnabled, demoAutoTradeNotionalInr, demoAutoTradeStrategy }` (first three may be null until saved; **`demoAutoTradeNotionalInr`** mirrors the user’s **wallet** balance (paper INR; same as **`GET …/wallet`**); **`demoAutoTradeStrategy`** is one of **`equal_split`**, **`confidence_weighted`**, **`high_conviction`**, **`one_signal_per_instrument`**, **`signal_strength_squared`**, **`implied_edge_weighted`**, **`one_signal_per_engine`**, **`top_half_confidence`** (allocation presets for the hypothetical EOD math only); **`mlAutomationEnabled`** on **GET** mirrors **`Users.FavoriteMlAutomationEnabled`** (**PUT …/chart-settings** and **PUT …/favorite-ml-automation** always persist **enabled** as **true**—client **`false`** is ignored) for **background** favorite ML; **`mlAutomationInterval`** is **m** (model candle size); **`mlAutomationPollIntervalMinutes`** is optional **N** (min whole minutes between **new** automation pass starts, stored as seconds in MySQL); when **N** is set, passes are spaced by **N** only and the job does **not** wait for the prior **m**-bar to close (merged **m**-candles are still validated before engines run); **`mlAutomationMinSecondsAfterBarOpen`** is an optional intrabar delay used **only when N is unset** (seconds after ref bar open; **null** = host **`FavoriteMlAutomation:MinSecondsAfterBarOpenForAutomation`**); **`zoomByInstrumentToken`** maps numeric Kite `instrument_token` strings to **visible bar count** for chart zoom, or null/omitted when empty); **`PUT /api/v1/broker/kite/instruments/chart-settings`** with `{ interval, rangePreset, graphType }` (all three required) and optional **`mlAutomationEnabled`** (accepted for compatibility; always saved as **true**) and optional **`trendAnalysisIntervals`** (string array of chart codes; omit to leave stored trend checkboxes unchanged) persists those fields on **`Users`** without clearing saved zoom. **`PUT /api/v1/broker/kite/instruments/favorite-ml-automation`** with `{ enabled }` (required; always stored as **true**) and optional **`interval`** (empty string clears the per-user automation bar interval so **`FavoriteMlAutomation:PredictionIntervalOverride`** / chart settings apply), **`pollIntervalMinutes`** (**N**: omit to leave unchanged; **0** clears; **1–1440** sets min whole minutes between **new** pass starts — when set, intrabar delay is not applied; pending rows still resolve every global cycle), and optional **`minSecondsAfterBarOpenForAutomation`** (omit to leave unchanged; JSON **null** clears the per-user override; **0**–**86400** sets seconds after bar open; see **`FavoriteMlAutomation:MinSecondsAfterBarOpenForAutomation`** for host default when unset). **`PUT /api/v1/broker/kite/instruments/chart-zoom`** with `{ instrumentToken, visibleBars }` (`visibleBars` null removes zoom for that token) persists zoom per instrument. **`PUT /api/v1/broker/kite/instruments/demo-auto-trade`** with `{ enabled, optional strategy }` persists **`Users.DemoAutoTradeEnabled`** and, when **`strategy`** is sent, **`Users.DemoAutoTradeStrategy`** (**`equal_split`** / **`confidence_weighted`** / **`high_conviction`** / **`one_signal_per_instrument`** / **`signal_strength_squared`** / **`implied_edge_weighted`** / **`one_signal_per_engine`** / **`top_half_confidence`** — hypothetical allocation presets only; not advice; no broker orders). Omit **`strategy`** to leave the stored preset unchanged. **`GET /api/v1/broker/kite/instruments/demo-auto-trade/eod-summary`** (optional **`?date=yyyy-MM-dd`** for a calendar day in **`FavoriteMlAutomation:ReportTimeZoneId`**, default **today**) returns a hypothetical same-day outcome from merged **`automation-recent`** rows **filtered to instrument tokens in the user’s trading locks** (`**GET /api/v1/broker/kite/trading-locks**`; **zero** locks ⇒ **no** demo rows) using the stored preset (directional legs: **next open→next close** when the resolved row includes **next open**, else **ref close→next close**; **neutral** legs get no allocation). Response includes **`demoAutoTradeLockedInstrumentCount`** and **gross** P&amp;L, **hypotheticalChargesInr** from host **`DemoAutoTrade:Charges`** (optional flat INR per allocated leg + turnover bps on allocated notional per leg; **Enabled** defaults **true** in template **`appsettings.json`**, **false** in **`appsettings.IntegrationTesting.json`**), and **net** **`hypotheticalTotalPnlInr`**. Requires `Authorization: Bearer …`. **`GET /api/v1/broker/kite/instruments/demo-auto-trade/today-legs`** (optional **`?date=yyyy-MM-dd`**, same report TZ and trading-lock filter as EOD) returns **`legs`**: one entry per merged automation row that day with **`status`** (e.g. **allocated** / **pending** / **excluded_***) plus **instrumentLotMultiplier** / **demoWholeLotsTraded** / **committedExposureApproxInr** / **hypotheticalBuyPrice** / **hypotheticalSellPrice** (Kite lot sizes from Locked for trading; INR slice floored to whole contracts at hypothetical entry; long/up vs short/down buy-sell convention) and INR **allocatedNotionalInr** / **legGrossPnlInr** / **legFeesInr** / **legNetPnlInr** when applicable. **`GET /api/v1/broker/kite/instruments/demo-auto-trade/full-report`** (optional **`fromUtc`** / **`toUtcExclusive`** as ISO UTC query params; half-open on **`PredictedAtUtc`**, span ≤ **93** days; omit **both** for the last **7** local calendar days in **`FavoriteMlAutomation:ReportTimeZoneId`**) returns JSON: demo / favorite-automation flags, allocation preset, per-day hypothetical P&amp;L (same rules as EOD and **same trading-lock row filter** as EOD, full notional applied each calendar day), summed hypothetical P&amp;L, direction counts, and outcome counts by **engine** and **candle interval** (row cap may set **`mayBeTruncated`**; **`demoAutoTradeLockedInstrumentCount`** mirrors EOD). SPA **Demo auto-trade** includes a **today-legs** table (**`GET …/demo-auto-trade/today-legs`**, polled about every **12s** while **Demo auto-trade** is open) plus **Full demo auto-trade report** (**Load last 7 days** / **Load merged log range** + **Download JSON**).
- **Kite orderbook**: `GET /api/v1/broker/kite/orders` returns the current day orderbook from Kite (`GET /orders`) with common final statuses (`OPEN`, `COMPLETE`, `CANCELLED`, `REJECTED`) and interim statuses (for example `VALIDATION PENDING`, `OPEN PENDING`, `TRIGGER PENDING`, `CANCEL PENDING`, `AMO REQ RECEIVED`) plus quantities, prices, IDs, and exchange/update timestamps. `GET /api/v1/broker/kite/positions/net` returns current non-zero net positions (exchange, symbol, product, signed quantity) from Kite `portfolio/positions`. **`GET /api/v1/broker/kite/margins`** returns equity and commodity funds from Kite **`GET /user/margins`** (`net`, `availableCash`, `liveBalance`, `openingBalance`, `intradayPayin`, `utilisedDebits` per segment); the SPA **Wallet** page shows this when Zerodha is linked. Order actions now proxy Kite too: `POST /api/v1/broker/kite/orders/place` (manual place from scalper/order ticket), `POST /api/v1/broker/kite/orders/{orderId}/cancel` (optional `variety`, `parentOrderId`), `POST /api/v1/broker/kite/orders/{orderId}/modify` (variety + order fields), and `POST /api/v1/broker/kite/orders/{orderId}/repeat` (optional `variety`, re-places the source order fields). For stop-loss flows, use `orderType` = `SL` (price + trigger) or `SL-M` (trigger only) with `triggerPrice`. For `MARKET` / `SL-M`, backend now sends `market_protection=-1` (auto) by default unless a custom `marketProtection` (`1..100`) is provided. **GTT (Good Till Triggered)**: `POST /api/v1/broker/kite/gtt` places a Kite GTT after entry — **two-leg OCO** when both **`stopLossEnabled`** and **`profitEnabled`** are true (default), or a **single-leg** GTT for stop-loss or profit target only. Body: `exchange`, `tradingsymbol`, `entryTransactionType` (`BUY`/`SELL`), `quantity`, `product`, optional `referencePrice` / `lastPrice`, optional `stopLossPrice` / `triggerPrice` overrides, optional `stopLossPercent` / `triggerPercent` (default **5** each), and optional **`stopLossEnabled`** / **`profitEnabled`** (default **true**). Response includes `triggerId`, `stopLossPrice`, and `targetPrice` (unused leg is **0** for single-leg).
- **Scalable broker order layer**: `GET /api/v1/broker/providers` lists currently supported order brokers (`zerodha`, `groww`) and connection state. Generic endpoints `POST /api/v1/broker/orders/place` and `GET /api/v1/broker/positions/net?broker=...` route by provider (or auto-pick a connected provider if omitted). Groww connection is now available via `POST /api/v1/broker/groww/connect` with either a direct `accessToken` or `apiKey` + (`apiSecret` or `totp`) so the server can create the daily token. Existing `.../broker/kite/...` routes continue to work for Kite-specific workflows.
- **Scalper preferences**: `GET /api/v1/broker/kite/scalper-settings` and `PUT /api/v1/broker/kite/scalper-settings` persist scalper UI defaults per user (interval/range/graph/volume + Safe Scalper toggle and N/M point values + **GTT stop-loss** / **GTT profit-target** toggles; legacy **`gttEnabled`** on GET is **true** when either leg is on).
- **Opening ATM (live)**: formerly “NIFTY Opening ATM” / “NIFTY open auto-trade”. When **`NiftyOpenAutoTrade:Enabled`** is **true**, a background worker near **09:15 IST** places one **MARKET BUY** of **ATM** index **CE**/**PE** (**`MIS`**) for the user’s selected underlying (**`nifty`**, **`banknifty`**, **`finnifty`**, **`midcpnifty`**, **`sensex`**, **`bankex`**) and preferred expiry (or nearest), sized to **max affordable lots** from Kite cash (up to **`maxLots`**, default/cap **10**, utilization default **1.0**). Exits: fixed **−ve/+ve** as **percent of entry premium** via one Kite **OCO GTT** when both on (`stopLossPercent` / `targetPercent`), or optional **trail SL** (`trailEnabled` — requires −ve; distance = same **`stopLossPercent`** below premium peak — single-leg SL GTT, ratcheted with **`PUT /gtt/triggers/:id`** until flat or ~**15:25 IST**; +ve is a separate GTT when trail is on). Prefs: **`GET`/`PUT /api/v1/broker/kite/nifty-open-auto-trade`**. Preview **`GET …/preview`**; runs **`GET …/runs`**. Dedicated section on **`/scalper`**. Migrations include **`AddOpeningAtmUnderlying`**, **`AddOpeningAtmTrailPrefs`**.
- **ML prediction (experimental)**: **`GET /api/v1/predictions/price-direction/models`** returns **`defaultModelId`** and **`models`** (`id`, `description`). **`GET /api/v1/predictions/price-direction?instrumentToken=…&interval=…`** supports optional **`model=<id>`** (Bearer; same `interval` codes); omit **`model`** to use **`PriceDirectionPrediction:DefaultModelId`** (**`mlnet-sdca-logistic-v1`** unless overridden in **`appsettings`** / **`PriceDirectionPrediction__DefaultModelId`**). Built-ins today: **ML.NET LightGBM** (**`mlnet-lightgbm-triple-barrier-v1`**; on-the-fly training on OHLC + backend SR/EMA with a **candle-bar** triple-barrier label), **ML.NET SDCA** logistic regression (**`mlnet-sdca-logistic-v1`**; short-horizon returns + SMA gap on closes), and a **momentum** baseline (**`momentum-close-v1`**). Add new engines in Infrastructure and register them in **`AddInfrastructure`**. Response adds optional **`predictionId`**, **`refBarTime`**, **`refClose`**, **`predictedAt`** when the run is **persisted**, and **`predictionStorage`** as **`classicPriceDirection`** or **`lightgbmTripleBarrier`** so clients know which MySQL table holds the row. **`GET /api/v1/predictions/price-direction/history?instrumentToken=…&interval=…&take=…`** returns classic-table rows (newest first, cap 2000). **`GET /api/v1/predictions/price-direction/lightgbm-triple-barrier/history?…`** returns LightGBM-only rows from **`MlLightGbmTripleBarrierPredictions`**. **`GET /api/v1/predictions/price-direction/automation-recent?take=…`** merges automation **`Source=automation`** rows from **both** tables (sorted by **`predictedAt`**; each row exposes **`engineModelId`** plus scoring **`modelId`**; **`take`** is clamped up to **5000** internally for merge headroom). Optional **`fromUtc`** and **`toUtcExclusive`** (ISO instants, UTC) filter on **`PredictedAtUtc`** in **`[fromUtc, toUtcExclusive)`** (same half-open window as the manual automation email; span ≤ **93** days; mixed omit/supply rejected). Omit both for legacy behavior (recent rows without a date filter). The **Auto predictions** SPA defaults the browser-local range to **today 00:00 → now** and passes these query params when the range is valid. **`POST /api/v1/predictions/price-direction/automation-report-email`** (Bearer; optional JSON **`{ fromUtc, toUtcExclusive }`** as ISO datetimes in **UTC**; rows match **`PredictedAtUtc`** in **[fromUtc, toUtcExclusive)**; omit **both** for **today** as a full calendar day in **`FavoriteMlAutomation:ReportTimeZoneId`**; span ≤ **93** days; mixed null/non-null rejected; requires **SMTP** + profile **email**) sends automation-only rows using **multipart email**: **HTML** parts with inline **PNG** outcome pies (**`cid:`**-linked images; combined + per engine) plus a **CSV** attachment; unlike the nightly EOD job this is **not** deduped in **`MlFavoriteEodReportsSent`**. SPA: **Email automation report** on **Auto predictions** (browser **`datetime-local`** range → **`fromUtc`** / **`toUtcExclusive`** POST body). **`PATCH /api/v1/predictions/price-direction/{id}/resolve`** with JSON **`{ "nextBarTime", "nextClose" }`** resolves a pending row in **either** table by id. Each table is capped per user (25k); oldest prune on insert for that store. The SPA shows **two** history panels (classic vs LightGBM) and resolves both against the chart series; **`localStorage`** fallback applies only when the classic history GET fails. Not investment advice; ML.NET path uses heuristic fallback if training fails or output is near 50%. **Optional automation:** when **`FavoriteMlAutomation:Enabled`** is **true** (off in default **`appsettings.json`**), a **background service** runs on **`PollIntervalSeconds`** when set (**&gt; 0**, clamped **1–3600**), otherwise every **`PollIntervalMinutes`** (**1–120**). Root **`appsettings.json`** includes **`PollIntervalSeconds`: 5** as a template; omit it (class default **0**) to keep minute-only polling. For each user who has **favorite** instruments **and** **`Users.FavoriteMlAutomationEnabled`**, it **resolves** pending rows when the next candle exists, then for each favorite, once the effective minimum seconds after the latest ref bar open have elapsed (per-user **`mlAutomationMinSecondsAfterBarOpen`** when set on **`Users`**, otherwise host **`FavoriteMlAutomation:MinSecondsAfterBarOpenForAutomation`**; **0** = no delay), invokes **every** registered engine (**`GET …/predictions/price-direction/models`**), persisting separately by store (LightGBM vs classic) with **`EngineModelId`** for dedupe — this does **not** wait for the full bar interval to finish. **Intraday** favorites also respect **`TradingSessionRestrictionsEnabled`** (default **09:15–15:30** local in **`ReportTimeZoneId`**, end exclusive) and coarse IST **minute** alignment via **`IstMinuteBoundaryAlignmentEnabled`** / **`IstMinuteBoundarySeconds`**; **`SkipTradingSessionForMcxFavorites`** (default **true**) exempts **`MCX`**; **daily/weekly** candles skip the session gate; the per-user **N**-minute cadence skips IST minute alignment only. Optional **`PredictionModelId`**: comma-separated engine ids **subset** only; omit, null, or empty = run **all** registered engines. **`PredictionIntervalOverride`** (default **`1m`**) applies when the user has not set a saved automation interval; leave **empty** to use saved chart settings (toolbar + per-favorite interval map). The SPA can persist a per-user automation interval and optional poll throttle via **`PUT …/favorite-ml-automation`**. **`BestOfThreeEnabled`** (default **true** in template **`appsettings.json`**) runs **three** sliding-window inferences on the same latest bar (drop 0/1/2 oldest candles) and stores the **majority** direction; the row’s **`detail`** begins with **`[b3 u=… d=… n=… v=… m=…]`**; nightly/manual automation emails add an aggregate **up/down/neutral** vote pie when any row carries that tag; CSV adds **`b3*`** columns. When there are fewer than **`MinCandlesRequired + 2`** candles, automation falls back to a **single** inference. New rows store **`Source=automation`**. When **`QuietHoursEnabled`** is **true** (default in **`FavoriteMlAutomation`**), the background job skips only **new** scheduled ML predictions for favorites during the local nightly window (**`QuietHoursStart/End*`** in **`ReportTimeZoneId`**; **`appsettings.json`** template stops **23:25** and resumes **08:00** IST with **`Asia/Kolkata`**); **`PauseAutomationOnWeekends`** (default **true**) skips **new** predictions all day on **Saturday/Sunday** in that TZ. **pending** automation rows **still resolve** when the next bar is available so correct/wrong can update overnight. **`LiveCandles`** and interactive API predictions are unaffected. Users **without a valid Kite session** are skipped. If **`Smtp:IsEnabled`** is **true**, once per **local** day (**`ReportTimeZoneId`**, default **`Asia/Kolkata`**) starting at **`ReportLocalHour`/`ReportLocalMinute`** (default **23:30 IST** via **`Asia/Kolkata`**, two-hour send window), the API emails that user **multipart/HTML** (inline **PNG** combined outcome chart via **`cid:`**) and a **CSV** listing **every** prediction across engines (**`engineModelId`**, **`modelId`**, outcomes) whose **`PredictedAtUtc`** falls on that calendar day (**`MlFavoriteEodReportsSent`** prevents duplicates). See **`FavoriteMlAutomation`** and **`PriceDirectionPrediction`** in **`appsettings.json`** and **`.env.example`**.

- **F&O multi-horizon model (`mlnet-fno-multi-horizon-v1`)**: profile-aware inference (1m/5m/15m inferred from candle spacing) with richer F&O features and confidence abstain guardrails. Optional tuning lives under **`PriceDirectionPrediction`** (`LabelThresholdFractionByInterval`, `MinTrainingRowsByInterval`, `NeutralConfidenceFloorByInterval`, `ScoreCalibrationJsonPathByInterval`). Keep rollout staged by switching **`PriceDirectionPrediction:DefaultModelId`** only after walk-forward promotion gates pass.

### Web (`frontend/`)

SPA paths: **`/scalper`** opens **Scalper** (multi-minute OHLC, locks/favorites + search, ~15s refresh + live ticks, **ML** direction ribbons on candles when prediction history exists for that symbol/interval; Zerodha) and includes in-page **GTT SL** / **GTT TP** toggles (independently enable stop-loss GTT, profit-target GTT, or both as OCO; default **5%** each when on; Safe mode uses point-based auto-risk controls: **N points** stop-loss and **M points** target auto-applied after entry via GTT, with draggable chart guides) plus **Opening ATM** (dedicated section: underlying, CE/PE, expiry, lot cap, Kite OCO −ve/+ve **%** and optional trail SL; live **MIS** ~**09:15 IST** — not demo/paper); **`/wallet`** opens **Wallet** (**Zerodha Kite** live margins when linked via **`GET …/broker/kite/margins`**; separate **paper** INR balance via **`GET …/wallet`** and manual top-up **`POST …/wallet/load`**—no payment gateway; **demo auto-trade** notional = paper **wallet** balance); **`/instruments?tab=favorites`** (or **`?tab=fav`**, **`?fav=1`**, **`?fav=true`**) opens **All favorites** (toolbar **Trend analysis** = multi-select of any chart intervals (`1m`–`1w`) for parallel past-window LR summaries via **`…/broker/kite/chart/historical-ohlc`**; selections persist to **`GET/PUT …/chart-settings`** `trendAnalysisIntervals` with **localStorage** as a fallback when nothing is saved yet); **Interval** still sets the single bar size on **all** charts and clears per-symbol overrides; each favorite tile shows **Multi-interval trend** under the chart with a loading state while OHLC fetches, plus a compact **Auto ML predictions** table for that symbol using the same **`GET …/automation-recent`** range as **Auto predictions** (refreshed on a **60s** poll while **All favorites** or **Locked for trading** is open, Zerodha linked)); **`?tab=locked`** (or **`?tab=trading-locks`**) opens **Locked for trading** (same grid as favorites, persisted under **`/broker/kite/trading-locks`**); **`?tab=manual-trade`** (**`/instruments/manual-trade`**; aliases **`manualtrade`**, **`manual`**, **`paper-trade`**, **`papertrade`**) opens **Manual paper trade** (wallet debits/credits at Kite LTP × **Locked for trading** lot size via **`POST …/demo-paper-trade`**; **`GET …/demo-paper-positions`**; **Scalper**-style OHLC chart + ticks for the chosen lock matching **`/scalper`** when Zerodha is linked; no broker orders); **`?tab=automation`** (**`/instruments/automation`**) opens **Auto predictions** (**Auto ML predictions**: favorite automation always remains enabled; per-user **bar interval** / **min minutes after previous new pass started**, merged log, charts, and the recent table for favorites in range; **direction vote** pie summarizing predicted **up** / **down** / **neutral** for the filtered automation rows, then per-engine **pie charts** for correct/wrong/pending (each tile also shows the engine’s **prediction accuracy %** = correct / (correct + wrong) over resolved rows in the current filter), **Direction** / **outcome** / **interval** / **engine** filter toggles (plus search); the recent-rows table renders **Category** (Browse-tab grouping resolved from the row's exchange — **F&O** for NFO/BFO, **Commodities** for MCX, **Spot** for NSE/BSE equity / indices) and **Conf %** (engine confidence per row, 0–100); the row search box matches against the category label too; **Email automation report** accepts a configurable **datetime range** (browser local → UTC **`fromUtc`** / **`toUtcExclusive`**) instead of relying on report-tz “today” only; the merged **recent** list uses the same range via **`GET …/automation-recent`** when valid; search—table and pies use the same filtered rows); **`?tab=demo-auto-trade`** (**`/instruments/demo-auto-trade`**) or **`tab=`** aliases **`demoautotrade`**, **`autotrading`**, **`auto-trading`**, **`demo-trade`** opens **Demo auto-trade** — **Wallet**-sized hypothetical notional, **manual paper** **`POST …/demo-paper-trade`** (buy/sell at LTP × lock lot), preset picker (**equal** / confidence-weighted / high-conviction / one-symbol / quadratic confidence / implied-edge / one-engine / top-half; illustrative only — not live trading), hypothetical **EOD** / **today-legs** / **full report** for **Locked for trading** instrument tokens only (**Full demo auto-trade report** **`GET …/demo-auto-trade/full-report`**, default **7** report-TZ days or same **From/To** as **Merged log range & email** on **Auto predictions** when configured); **`/instruments/fav`** redirects to **`?tab=favorites`**; **`/instruments/manual-trade`** redirects to **`?tab=manual-trade`**; **`/instruments/locked`** redirects to **`?tab=locked`**. **`/instruments`** without **`tab`** is **Browse**. Switching tabs updates the query with **`replace`**. On **Browse**, **Today's top performers** ranks capped preview contracts by OHLC-derived % vs the prior session (fetches up to **30** server-side, **5** rows visible at a time with **Load more**) and supports opening the chart from a row.

| File | Purpose |
|------|---------|
| `.env.development` | Optional `VITE_DEV_SERVER_PORT`, `VITE_API_PROXY_TARGET`; same-origin **`/api`** / **`/hubs`** is the default. |
| `.env.production` | Same-origin **`/api`** / **`/hubs`** is the default. Use **`VITE_FORCE_CROSS_ORIGIN_API=true`** + **`VITE_API_BASE_URL`** only for a deliberate split-host deployment. |

See `frontend/.env.example`. Only variables prefixed with `VITE_` are exposed to the browser.

The SPA uses **React Bootstrap** (components) and **Bootstrap 5** (CSS). `index.html` defaults **`data-bs-theme="dark"`**; on load **`main.tsx`** applies the saved **Profile → Appearance** choice (light / dark / system) from **`localStorage`** (`**trader-theme-preference**`) so the whole app, including **login**, matches the user’s mode.

Time-series OHLC dashboards (instrument strips, cumulative/fill **P&amp;L** curves, histogram-style bars such as trades and demo reports) render with **[TradingView Lightweight Charts™](https://tradingview.github.io/lightweight-charts/)** via **`npm` dependency `lightweight-charts`**; **pie** summaries (direction vote and per-engine outcomes on **Auto predictions**) remain on **Recharts**.

Example layout for production: see **[Deploy to DigitalOcean App Platform](#deploy-to-digitalocean-app-platform)** and **`.do/app.yaml`**.

## Deploy to DigitalOcean App Platform

High-level path: **Managed MySQL** + **one App** from this monorepo with a **Web Service** (API) and a **Static Site** (frontend), plus **ingress** so **`/api` → API**, **`/hubs` → API**, and **`/` → static**. TLS is at the edge; the .NET container listens on HTTP **8080** internally (match **`http_port`** in the spec).

### 1. Database

- Create a **Managed MySQL** cluster (same region as the app helps latency).
- In **Connection details**, copy the **`mysql://…`** URL (or build one with host, port **25060**, database **`defaultdb`**, user **`doadmin`**, password, **`?ssl-mode=Required`**). Set it as **`DATABASE_URL`** on the API service (see step 4).

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
| **`DATABASE_URL`** | **Required.** `mysql://user:pass@host:port/db?ssl-mode=Required` from Managed MySQL **Connection details** (**Encrypt** in the UI). |
| **`Jwt__Issuer`**, **`Jwt__Audience`**, **`Jwt__Key`** | **`Jwt__Key`** ≥ 32 chars; **secret**. |
| **`Jwt__ExpiresHours`** (optional) | Access token lifetime in hours (default **12** in **`appsettings.json`**). Shorter values (e.g. **1**) cause **`SecurityTokenExpiredException`** in logs after idle time; users must sign in again or you can raise this in production. |
| **`Cors__Origins__0`** | Your live site origin, e.g. **`https://your-app.ondigitalocean.app`** (no trailing slash). |
| **`ZerodhaKite__*`** | If you use Kite: **secrets**; **`RedirectUrl`** must be the public **`https://…/api/v1/broker/kite/callback`**. |
| **`Groww__ApiBaseUrl`** (optional) | Defaults to `https://api.groww.in/v1/`. Override only for sandbox/proxy setups. Groww user API keys/tokens are entered from Profile and stored encrypted server-side. |

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

  Use the same **`DATABASE_URL`** (or alias) as production so migrations hit the managed DB. **Image builds** use **`/p:RunEfMigrationsOnBuild=false`**; **production** still applies migrations **on API startup** unless you set **`Database__ApplyMigrationsOnStartup=false`**.

**Troubleshooting — `Unknown column '...KiteInstrumentsChartZoomJson'` or **`...KiteInstrumentsChartIntervalByInstrumentTokenJson`** (login/register/forgot-password 500):** The model includes the matching migration, but the live **`Users`** table in the catalog the API uses (logs show **`TABLE_SCHEMA='defaultdb'`**) never got the column while **`__EFMigrationsHistory`** already lists that migration—so **`Migrate()`** reports *already up to date* and does nothing. Repair on that database (DigitalOcean MySQL console or any client), idempotent on MySQL 8+:

```sql
ALTER TABLE `Users`
  ADD COLUMN IF NOT EXISTS `KiteInstrumentsChartZoomJson` longtext NULL;
ALTER TABLE `Users`
  ADD COLUMN IF NOT EXISTS `KiteInstrumentsChartIntervalByInstrumentTokenJson` longtext NULL;
```

On older MySQL, run a plain **`ADD COLUMN`** once, or use **`SHOW COLUMNS FROM Users LIKE 'KiteInstrumentsChartZoomJson';`** (and the interval column name) to confirm before adding. Ensure you are connected to the **same** schema as in **`DATABASE_URL`** (managed clusters often use **`defaultdb`**).

### 7. Smoke test

- **`GET /api/health`** (or **`GET /health`** if you call the API service directly). On a **single hostname** with ingress, **`/health`** alone often hits the **static site**, so prefer **`/api/health`** for checks through the edge.
- Open the static URL, sign in, confirm **`/api/v1`** calls succeed (browser devtools **Network** tab).
- If **`/api`** routes return **404**, fix ingress (**`preserve_path_prefix`**) and rule order. If deep links **404**, set the static site **Catchall** to **`index.html`**.

**Troubleshooting — `JWT is not configured` / readiness `connection refused` on 8080:** The API process crashes **before** Kestrel listens, so probes fail. Add **`Jwt__Issuer`**, **`Jwt__Audience`**, and **`Jwt__Key`** (≥ 32 characters) to the **Web Service** component with scope **RUN_TIME** (not only BUILD_TIME, and not only on the static site). Add **`Cors__Origins__0`** the same way or the next startup error will be about CORS. Names must use **`__`** (e.g. **`Jwt__Key`**), not colons.

**Troubleshooting — MySQL connection / configuration missing:** On the **Web Service**, set **`DATABASE_URL`** (**RUN_TIME**, **Encrypt**) — `mysql://user:pass@host:25060/defaultdb?ssl-mode=Required` from Managed MySQL **Connection details**. Also set **`Database__Provider=MySQL`**. See **`backend/src/Trader.Api/.env.production`**.

**Troubleshooting — registration / forgot-password email not sent (`5.7.0 Authentication Required`, HTTP 400):** Set **`Smtp__IsEnabled=true`**, **`Smtp__Host=smtp.gmail.com`**, **`Smtp__Port=587`**, **`Smtp__User`**, **`Smtp__FromEmail`**, and **`Smtp__Password`** to a Gmail **App password** (not your normal login password). Startup should log **`Outbound email via SMTP smtp.gmail.com:587`**. On some hosts outbound SMTP can still be blocked — check API logs for auth/connect errors. **`Development`** with SMTP off logs OTP text to the API console. Also set **`PublicWeb__FrontendBaseUrl`** for registration verify links. **`POST /api/v1/auth/forgot-password`** returns **204** even when no account exists; a **~100 ms** response usually means the email is not registered.

**Troubleshooting — App Platform / Heroku build: `MSB4018` / `GenerateDepsFile` / `deps.json` in use:** The buildpack runs **`dotnet publish`** with a shared **`--artifacts-path`**, which can deadlock or race when MSBuild compiles shared projects on multiple nodes. This repo sets **`BuildInParallel=false`** in **`backend/Directory.Build.props`** so publish is single-threaded (slightly slower, stable on App Platform).

**Troubleshooting — `You must install or update .NET` / Framework `8.0.0` not found (only `10.x` present):** The app targets **`net8.0`**. Keep **`backend/global.json`** on the **8.0** band with **`rollForward`: `latestFeature`** (not **`latestMajor`**). **`latestMajor`** lets the buildpack install SDK/runtime **10** while the published app still requires **Microsoft.NETCore.App 8.0**, so the process exits before Kestrel listens and readiness probes fail.

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
