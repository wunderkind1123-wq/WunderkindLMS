# Build the SPA, then serve the static bundle with Caddy (SPA fallback in Caddyfile).
FROM node:20-alpine AS build
WORKDIR /app
COPY package.json package-lock.json* ./
RUN npm ci || npm install
COPY . .
# Brand shown in the panel; empty -> Wunderkind (defaults in vite.config.ts)
ARG VITE_BRAND_NAME=
ARG VITE_BRAND_APP_NAME=
# Path prefix of the panel: empty -> site root; WunderkindLMS builds it with /chatbot/
ARG VITE_BASE=
RUN npm run build

FROM caddy:2-alpine
# Caddyfile (standalone server) or Caddyfile.lms (inside WunderkindLMS)
ARG CADDYFILE=Caddyfile
COPY ${CADDYFILE} /etc/caddy/Caddyfile
COPY --from=build /app/dist /srv
EXPOSE 80
