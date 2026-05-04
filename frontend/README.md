# Trader web (frontend)

Vite + React SPA. Configure **`VITE_API_BASE_URL`** in **`.env.development`** / **`.env.production`**.

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
