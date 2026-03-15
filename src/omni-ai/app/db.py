"""
Database clients for ClickHouse (read) and PostgreSQL (write).
"""
import os
from contextlib import asynccontextmanager
import clickhouse_connect
import sqlalchemy
from sqlalchemy.engine import Engine

# Env-based config
CLICKHOUSE_HOST = os.getenv("CLICKHOUSE_HOST", "localhost")
CLICKHOUSE_PORT = int(os.getenv("CLICKHOUSE_PORT", "8123"))
CLICKHOUSE_USER = os.getenv("CLICKHOUSE_USER", "default")
CLICKHOUSE_PASSWORD = os.getenv("CLICKHOUSE_PASSWORD", "")
CLICKHOUSE_DB = os.getenv("CLICKHOUSE_DB", "omni_analytics")
def _normalize_pg_url(url: str) -> str:
    """Use postgresql+psycopg2 so SQLAlchemy can load the dialect (postgres:// → postgresql+psycopg2://)."""
    if not url:
        return url
    if url.startswith("postgres://"):
        return "postgresql+psycopg2://" + url[len("postgres://") :]
    if url.startswith("postgresql://") and "+" not in url.split("://")[0]:
        return url.replace("postgresql://", "postgresql+psycopg2://", 1)
    return url


_raw_database_url = os.getenv("DATABASE_URL", os.getenv("POSTGRES_URL", ""))
DATABASE_URL = _normalize_pg_url(_raw_database_url)

# Module-level clients (set at startup, closed at shutdown)
_clickhouse_client: clickhouse_connect.driver.Client | None = None
_pg_engine: Engine | None = None


def get_clickhouse_client() -> clickhouse_connect.driver.Client | None:
    """Return the shared ClickHouse client, or None if not configured."""
    return _clickhouse_client


def get_pg_engine() -> Engine | None:
    """Return the shared PostgreSQL engine, or None if not configured."""
    return _pg_engine


def init_clients() -> None:
    """Create ClickHouse and PostgreSQL clients from env. Idempotent."""
    global _clickhouse_client, _pg_engine
    if _clickhouse_client is None and CLICKHOUSE_HOST:
        _clickhouse_client = clickhouse_connect.get_client(
            host=CLICKHOUSE_HOST,
            port=CLICKHOUSE_PORT,
            username=CLICKHOUSE_USER,
            password=CLICKHOUSE_PASSWORD or None,
            database=CLICKHOUSE_DB,
        )
    if _pg_engine is None and DATABASE_URL:
        _pg_engine = sqlalchemy.create_engine(DATABASE_URL, pool_pre_ping=True)


def close_clients() -> None:
    """Close ClickHouse and PostgreSQL connections."""
    global _clickhouse_client, _pg_engine
    if _clickhouse_client is not None:
        _clickhouse_client.close()
        _clickhouse_client = None
    if _pg_engine is not None:
        _pg_engine.dispose()
        _pg_engine = None
