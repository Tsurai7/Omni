# ClickHouse schema for telemetry events

The analytical pipeline stores telemetry events in ClickHouse for fast aggregations and AI queries.

## Database and table

- **Database:** `omni_analytics` (created if not exists).
- **Table:** `telemetry_events` — single MergeTree table for both usage and session events.

## Table definition

```sql
CREATE TABLE IF NOT EXISTS omni_analytics.telemetry_events (
    event_id UUID,
    event_type String,
    user_id UUID,
    at DateTime64(3),
    recorded_at Nullable(DateTime64(3)),
    started_at Nullable(DateTime64(3)),
    app_name Nullable(String),
    category Nullable(String),
    name Nullable(String),
    activity_type Nullable(String),
    duration_seconds Int64
) ENGINE = MergeTree()
ORDER BY (user_id, at, event_id);
```

- `at`: event time used for ordering (same as `recorded_at` for usage, `started_at` for session).

- `event_type`: `usage` or `session`.
- Usage events: `recorded_at`, `app_name`, `category`, `duration_seconds` are set; session fields are null.
- Session events: `started_at`, `name`, `activity_type`, `duration_seconds` are set; usage fields are null.

## Data source

Events are produced by the Go Telemetry service (after persisting to PostgreSQL) to the Redpanda topic `omni.telemetry.events`. The **telemetry-consumer** Go service consumes from that topic and batch-inserts into this table.
