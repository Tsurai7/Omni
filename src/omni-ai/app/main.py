"""
Omni AI microservice — health and future burnout/recommendations.
Phase 4.1: FastAPI with ClickHouse (read) and PostgreSQL (write) connections.
"""
import sys
from contextlib import asynccontextmanager

from fastapi import FastAPI

from app.db import close_clients, get_clickhouse_client, get_pg_engine, init_clients


@asynccontextmanager
async def lifespan(app: FastAPI):
    init_clients()
    print("omni-ai service started successfully", flush=True)
    sys.stdout.flush()
    yield
    close_clients()


app = FastAPI(title="Omni AI", version="0.1.0", lifespan=lifespan)


@app.get("/health")
def health():
    """Health check for orchestration and load balancers."""
    return {"status": "ok", "service": "omni-ai"}


@app.get("/internal/telemetry-stats")
def telemetry_stats():
    """
    Simple read from ClickHouse to verify connectivity.
    Returns row count and latest event time for omni_analytics.telemetry_events.
    """
    client = get_clickhouse_client()
    if client is None:
        return {"error": "ClickHouse not configured", "count": None, "latest_at": None}
    try:
        r = client.query(
            "SELECT count() AS cnt, max(at) AS latest FROM omni_analytics.telemetry_events"
        )
        row = r.result_set[0] if r.result_set else (0, None)
        return {"count": row[0], "latest_at": str(row[1]) if row[1] else None}
    except Exception as e:
        return {"error": str(e), "count": None, "latest_at": None}
