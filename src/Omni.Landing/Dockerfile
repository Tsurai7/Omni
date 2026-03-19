# Build static site, serve with nginx.
FROM node:22-alpine AS build
WORKDIR /app

ENV ASTRO_TELEMETRY_DISABLED=1

ARG PUBLIC_DOWNLOAD_MAC_URL=https://example.com/omni-macos
ARG PUBLIC_DOWNLOAD_WIN_URL=https://example.com/omni-windows
ARG PUBLIC_APP_VERSION=

ENV PUBLIC_DOWNLOAD_MAC_URL=$PUBLIC_DOWNLOAD_MAC_URL
ENV PUBLIC_DOWNLOAD_WIN_URL=$PUBLIC_DOWNLOAD_WIN_URL
ENV PUBLIC_APP_VERSION=$PUBLIC_APP_VERSION

COPY package.json package-lock.json ./
RUN npm ci

COPY . .
RUN npm run build

FROM nginx:alpine
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 80
