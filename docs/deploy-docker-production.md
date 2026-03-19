# Production Docker Compose (single server)

The stack is defined in [`docker-compose.production.yml`](../docker-compose.production.yml): PostgreSQL, ClickHouse, Redpanda, Go microservices (gateway, profile, task, telemetry, telemetry-consumer), the Python AI service, and the static landing site.

## Quick start

1. Copy [`../.env.production.example`](../.env.production.example) to `.env.production` and set strong values for `POSTGRES_PASSWORD`, `CLICKHOUSE_PASSWORD`, and `JWT_SECRET` (32+ characters for the JWT secret).
2. From the repo root:

   ```bash
   docker compose -f docker-compose.production.yml --env-file .env.production build
   docker compose -f docker-compose.production.yml --env-file .env.production up -d
   ```

   Or: `make prod-build` and `make prod-up` (same requirements).

3. **Gateway** (API): `http://<host>:${BACKEND_PORT:-9080}`  
   **Landing**: `http://<host>:${LANDING_PORT:-3000}`  
   **AI** is bound to **127.0.0.1** on the server only (`${AI_PORT:-8000}`); reach it via SSH tunnel or a reverse proxy on the same machine.

## Security notes

- Postgres and ClickHouse listen on **127.0.0.1** only on the host. Redpanda Kafka/schema registry ports are **not** published; only containers on the `omni` network can use them.
- Optional **Redpanda Console** (debugging): `docker compose -f docker-compose.production.yml --env-file .env.production --profile admin up -d` — console UI is bound to `127.0.0.1:${REDPANDA_CONSOLE_PORT:-8088}`.
- Terminate TLS on a reverse proxy (Caddy, nginx, Traefik) in front of **gateway** and **landing**; do not expose the database ports publicly.
- Back up Docker volumes `omni_pgdata`, `clickhouse_data`, and `redpanda_data` on a schedule.

## Redpanda

This file uses the same broker command as local infrastructure (`dev-container` mode) for compatibility on a single node. For larger or strict production streaming requirements, plan a dedicated Redpanda/Kafka deployment or a managed offering and point `KAFKA_BROKERS` at it (compose changes would be required).

## Images

Services use `build` plus an `image:` name so you can retag and push to a registry if you move off build-on-server.
