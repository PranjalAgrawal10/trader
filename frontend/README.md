# Trader web (frontend)

Vite + React SPA. API calls use same-origin **`/api`** by default to avoid CORS preflight **`OPTIONS`**. In dev, Vite proxies **`/api`** and **`/hubs`** to the API; for the Docker image, nginx does the same.

```bash
npm install
npm run dev
npm run build
```

See the repository root **`README.md`** for Docker Compose (full stack) and App Platform. Build the UI image with **`frontend/Dockerfile`** (used by Compose **`web`**).

```bash
# from repo root
docker compose build web
```
