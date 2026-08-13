# syntax=docker/dockerfile:1
#
# The terminal is a static single-page application. It is compiled once and
# then served by nginx, which is both simpler and closer to how it would be
# deployed than running the Vite dev server in a container.

# --- Build -----------------------------------------------------------------
FROM node:24-alpine AS build
WORKDIR /app

# npm ci needs both files and installs exactly what the lock file pins.
COPY package.json package-lock.json ./
RUN npm ci

COPY . .

# Vite inlines VITE_* variables at build time, so the API URL is a build
# argument rather than a runtime environment variable.
ARG VITE_API_BASE_URL=""
ENV VITE_API_BASE_URL=$VITE_API_BASE_URL

RUN npm run build

# --- Runtime ---------------------------------------------------------------
FROM nginx:1.29-alpine AS runtime

COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 80

# nginx's own health path, used by the Compose health check.
HEALTHCHECK --interval=10s --timeout=3s --start-period=5s --retries=5 \
    CMD wget --quiet --tries=1 --spider http://localhost/healthz || exit 1
