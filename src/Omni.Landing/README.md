# Omni.Landing

Static download landing for **Omni** (macOS and Windows). Built with [Astro](https://astro.build/) as a fully static site.

## Develop

```bash
cd src/Omni.Landing
npm install
npm run dev
```

Open [http://localhost:4321](http://localhost:4321).

If `astro build` fails with a permissions error under `~/Library/Preferences/astro`, disable telemetry:

```bash
export ASTRO_TELEMETRY_DISABLED=1
npm run build
```

## Configure download URLs

At **build time**, Astro inlines `PUBLIC_*` variables into the static HTML.

1. Copy `.env.example` to `.env` and set URLs, or export variables in the shell.
2. Rebuild.

| Variable | Purpose |
|----------|---------|
| `PUBLIC_DOWNLOAD_MAC_URL` | macOS installer URL |
| `PUBLIC_DOWNLOAD_WIN_URL` | Windows installer URL |
| `PUBLIC_APP_VERSION` | Optional version string under the buttons |

Defaults in code point at `https://example.com/omni-macos` and `omni-windows` until you set real artifact links (e.g. GitHub Releases, S3, CDN).

## Shipping new app versions

**Recommended:** host installers on **GitHub Releases** (or S3/CDN) using **fixed filenames** and stable “latest” URLs so you only upload new binaries each release — the landing does not need a rebuild. See **[`docs/releases.md`](../../docs/releases.md)** for the full checklist, filename conventions, and when to set `PUBLIC_APP_VERSION`.

**CI:** [`.github/workflows/landing.yml`](../../.github/workflows/landing.yml) verifies builds on landing changes and, on **release published**, rebuilds with the release tag as `PUBLIC_APP_VERSION` and uploads `dist/` as the `landing-dist` artifact.

## Production build

```bash
npm run build
```

Output is in `dist/`. Deploy `dist/` to any static host (Cloudflare Pages, Netlify, Vercel, S3 + CloudFront, etc.).

Update `site` in `astro.config.mjs` to your real origin so canonical/OG URLs are correct.

## Docker (nginx)

From the **repository root**:

```bash
docker compose -f docker-compose.infrastructure.yml -f docker-compose.services.yml build landing
docker compose -f docker-compose.infrastructure.yml -f docker-compose.services.yml up -d landing
```

The page is served on [http://127.0.0.1:3000](http://127.0.0.1:3000) by default (`LANDING_PORT` overrides the host port).

Pass download URLs at **image build** time (compose `build.args` or `docker build --build-arg`):

- `PUBLIC_DOWNLOAD_MAC_URL`
- `PUBLIC_DOWNLOAD_WIN_URL`
- `PUBLIC_APP_VERSION` (optional)

Or set the same variables in a root `.env` used by Compose when building.

## With full stack

`make services-up` / `make up` (when using `docker-compose.services.yml`) starts **landing** along with backend services. It does not depend on Postgres or the gateway.
