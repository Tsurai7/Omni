# Omni Backend

Go microservices for Omni: API Gateway, Profile (auth), Task, and Telemetry. See `docs/ai/architecture.md` and `docs/ai/implementation_plan.md` for the full plan.

## Services

| Binary | Role | Routes |
|--------|------|--------|
| **gateway** | Single REST entry; validates JWT and reverse-proxies to services | `/api/auth/*` → profile, `/api/usage/*`, `/api/sessions/*` → telemetry, `/api/tasks/*` → task |
| **profile** | Auth, registration, JWT, users | `POST/GET /api/auth/register`, `/login`, `/me` |
| **task** | Task CRUD (stub in place; full implementation in Phase 2.1) | `GET/POST /api/tasks`, `PATCH /:id/status`, `DELETE /:id` |
| **telemetry** | Usage and sessions ingest; later RedPanda producer | `POST/GET /api/usage/sync`, ``, `/api/sessions/sync`, `` |

All services share the same PostgreSQL (transactional). The client talks only to the **gateway** (port 8080).

## Requirements

- Go 1.26+
- PostgreSQL (e.g. 14+)

## Environment

**Gateway** (no database):

| Variable | Description |
|----------|-------------|
| `PORT` | HTTP port (default: 8080) |
| `JWT_SECRET` | Secret for validating JWTs |
| `PROFILE_URL` | Profile service base URL (e.g. `http://localhost:8081`) |
| `TASK_URL` | Task service base URL (e.g. `http://localhost:8082`) |
| `TELEMETRY_URL` | Telemetry service base URL (e.g. `http://localhost:8083`) |

**Profile / Task / Telemetry** (each needs DB + JWT):

| Variable | Description |
|----------|-------------|
| `PORT` | Service port (e.g. 8081, 8082, 8083) |
| `DATABASE_URL` | PostgreSQL connection string |
| `JWT_SECRET` | Same as gateway (for issuing/validating tokens) |
| `JWT_EXPIRY_HOURS` | Token lifetime (default: 24) |

## Run locally (multi-service)

From `src/Omni.Backend` with PostgreSQL running and `.env` set:

```bash
# Terminal 1 – profile
PORT=8081 go run ./cmd/profile

# Terminal 2 – task
PORT=8082 go run ./cmd/task

# Terminal 3 – telemetry
PORT=8083 go run ./cmd/telemetry

# Terminal 4 – gateway (client talks to this)
PROFILE_URL=http://localhost:8081 TASK_URL=http://localhost:8082 TELEMETRY_URL=http://localhost:8083 go run ./cmd/gateway
```

Gateway listens on `http://localhost:8080`.

## Run locally (monolith, dev shortcut)

Single process with all routes (no proxy):

```bash
go run ./cmd/server
```

Uses the same `DATABASE_URL` and `JWT_*`; no `PROFILE_URL` etc.

## Docker Compose (repo root)

From the **repository root**:

```bash
# Set at least JWT_SECRET (e.g. copy from docker-compose.env.example)
export JWT_SECRET=your-secret-min-32-chars

docker compose up -d
```

- **Gateway**: exposed on host as `http://localhost:9080` by default (`BACKEND_PORT` in `.env`). To use the client’s default `BaseUrl` (`http://localhost:8080`), set `BACKEND_PORT=8080` in `.env` before `docker compose up`. Profile, task, telemetry run on the Docker network and are not exposed.

### Coach / `/api/ai/*` returns 404

The gateway proxies `/api/ai/*` to **omni-ai** via **`AI_URL`** (Compose sets `http://ai:8000` by default). A **404** on `/api/ai/chat/...` or `/api/ai/focus-score/...` almost always means **`AI_URL` points at the wrong process** (e.g. profile `8081` or telemetry) or omni-ai is not running. For a **local** gateway on the host, use `AI_URL=http://127.0.0.1:8000` (see `Makefile` `run-gateway`). The client **`Backend:BaseUrl`** must be the **gateway root only** — not `.../api` — or paths become `/api/api/...` and return 404.

**Do not** put `/api/ai` on `AI_URL` (e.g. not `http://localhost:8000/api/ai`): the reverse proxy joins the base path with the full request path, which would double the prefix and yield 404. The gateway **strips** a trailing `/api/ai` from `AI_URL` at startup and logs a hint if it did.

On startup, when `AI_URL` is set, the gateway **blocks** until `GET {AI_URL}/health` returns 200 with body containing `omni-ai`, so a wrong target fails fast instead of failing at first coach request. Set **`AI_SKIP_HEALTHCHECK=1`** to disable (e.g. unusual deployments). For local coach, run omni-ai with **`make run-ai`** (repo root); **`make run-all-backend`** does not start it.

## API (via gateway)

- **POST /api/auth/register** — Create account. Body: `{"email":"...","password":"..."}`. Returns user + JWT.
- **POST /api/auth/login** — Login. Body: `{"email":"...","password":"..."}`. Returns JWT.
- **GET /api/auth/me** — Current user. Header: `Authorization: Bearer <token>`.
- **POST /api/usage/sync**, **GET /api/usage** — Usage telemetry (auth required).
- **POST /api/sessions/sync**, **GET /api/sessions** — Sessions (auth required).
- **GET /api/tasks**, **POST /api/tasks**, etc. — Task CRUD stub (auth required).

## Example

```bash
# Register
curl -s -X POST http://localhost:8080/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{"email":"you@example.com","password":"password123"}' | jq

# Login
curl -s -X POST http://localhost:8080/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"you@example.com","password":"password123"}' | jq

# Me (replace TOKEN)
curl -s http://localhost:8080/api/auth/me -H "Authorization: Bearer TOKEN" | jq
```

Implementation checklist and phases: `docs/ai/implementation_plan.md`.
