# Agent & contributor notes

Facts that stay stable across chats; **architecture rules live in `.cursor/rules/`** — especially **`core-principles.mdc`** (SOLID mapped to this solution’s layers).

## Layout

| Area | Path |
|------|------|
| API host | `backend/src/Trader.Api` |
| API bootstrap & pipeline | `backend/src/Trader.Api/Hosting` |
| API routing (version prefix, health, SignalR map) | `backend/src/Trader.Api/Routing` |
| V1 controllers | `backend/src/Trader.Api/Controllers/V1` |
| Broker HTTP surface (partial controller) | `backend/src/Trader.Api/Controllers/V1/Broker` |
| Application (use cases, DTOs) | `backend/src/Trader.Application` |
| Broker use cases & DTOs | `backend/src/Trader.Application/Broker` (partials + `Dtos/`) |
| Kite HTTP client (infra) | `backend/src/Trader.Infrastructure/Broker` (partials) |
| Ports / abstractions | `backend/src/Trader.Application/Abstractions` |
| Domain | `backend/src/Trader.Domain` |
| EF Core, infra, migrations | `backend/src/Trader.Infrastructure` |
| Integration tests | `backend/tests/Trader.Tests` |
| SPA | `frontend/` |

## Backend

- Solution: `backend/Trader.sln`
- Prefer **`-p:RunEfMigrationsOnBuild=false`** when MySQL or `dotnet-ef` is unavailable (compile-only, CI).
- **Do not rewrite** shipped EF migration history; add new migrations per `.cursor/rules/ef-core-migrations.mdc`.

```bash
cd backend
dotnet build Trader.sln -p:RunEfMigrationsOnBuild=false
dotnet test Trader.sln -p:RunEfMigrationsOnBuild=false
```

## Frontend

**Timestamps in the SPA** (tables, charts, tooltips): `formatLocalDateTime` in `frontend/src/utils/formatLocalDateTime.ts` — **`DD/MM/YY HH.mm.ss.fff`**, local timezone. Rule: `.cursor/rules/datetime-display.mdc`.

```bash
cd frontend
npm ci
npm run typecheck
npm run build
```

## CI

GitHub Actions runs **`.github/workflows/ci.yml`**: backend build + test (no DB at build time), frontend `npm ci`, typecheck, production build.
