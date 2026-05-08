# Build static assets (Vite). By default the browser calls same-origin /api and /hubs, and nginx
# (final stage) proxies them to the API. Override only when you intentionally want the bundle
# to call a separate API origin (split-host deployment).
FROM node:20-alpine AS build
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci

COPY . .

ARG VITE_FORCE_CROSS_ORIGIN_API=
ENV VITE_FORCE_CROSS_ORIGIN_API=$VITE_FORCE_CROSS_ORIGIN_API
ARG VITE_API_BASE_URL=
ENV VITE_API_BASE_URL=$VITE_API_BASE_URL

RUN npm run build

FROM nginx:1.27-alpine

# Render /etc/nginx/conf.d/default.conf at container start via the image's envsubst entrypoint
# (`/docker-entrypoint.d/20-envsubst-on-templates.sh`). NGINX_ENVSUBST_FILTER limits substitution
# to *our* env vars so nginx variables like $host / $proxy_add_x_forwarded_for are left alone.
COPY nginx.conf.template /etc/nginx/templates/default.conf.template
ENV API_UPSTREAM=http://api:8080
ENV NGINX_ENVSUBST_FILTER=^API_UPSTREAM$

COPY --from=build /app/dist /usr/share/nginx/html

EXPOSE 80
