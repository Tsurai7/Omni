# Omni AI Microservice

Base for the AI/burnout-detection microservice (Phase 4 of the implementation plan).

## Setup

- **Python**: 3.12+
- Install: `pip install -e .` or `pip install fastapi uvicorn clickhouse-connect sqlalchemy pandas psycopg2-binary scikit-learn`

## Run locally

```bash
uvicorn app.main:app --reload --port 8000
```

## Environment

| Variable | Description |
|----------|-------------|
| `CLICKHOUSE_HOST` | ClickHouse host (default: localhost) |
| `CLICKHOUSE_PORT` | ClickHouse HTTP port (default: 8123) |
| `CLICKHOUSE_USER` | ClickHouse user |
| `CLICKHOUSE_PASSWORD` | ClickHouse password |
| `DATABASE_URL` / `POSTGRES_URL` | PostgreSQL connection string for writing recommendations |
| `CLICKHOUSE_DB` | ClickHouse database (default: omni_analytics) |

ClickHouse schema (table `telemetry_events`) is documented in [docs/ai/clickhouse_schema.md](../../docs/ai/clickhouse_schema.md). Data is populated by the telemetry-consumer from the Redpanda topic `omni.telemetry.events`.

## Endpoints

- `GET /health` — Health check.

Future: anomaly detection, scheduler, and writing to `user_notifications` in PostgreSQL (see `docs/ai/implementation_plan.md` Phase 4).
