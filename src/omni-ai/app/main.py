"""
Omni AI microservice — health and future burnout/recommendations.
Phase 4.1: FastAPI with ClickHouse (read) and PostgreSQL (write) connections.
"""
import sys
from contextlib import asynccontextmanager
from uuid import UUID

from fastapi import FastAPI, HTTPException

from app.db import close_clients, get_clickhouse_client, get_pg_engine, init_clients
from app.scheduler import run_recommendation_job_once, start_scheduler, stop_scheduler
from app.simulate import SimulationRequest, run_simulation


@asynccontextmanager
async def lifespan(app: FastAPI):
    init_clients()
    start_scheduler()
    print("omni-ai service started successfully", flush=True)
    sys.stdout.flush()
    yield
    stop_scheduler()
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


@app.post("/internal/run-recommendations")
def trigger_recommendations():
    """
    Run the recommendation job once immediately (for testing).
    Does not require auth. Returns active_users, notifications_written, and a sample.
    """
    result = run_recommendation_job_once()
    return result


@app.post("/internal/simulate")
def simulate_recommendations(req: SimulationRequest):
    """
    Simulate the recommendation engine with preset or custom inputs. No DB required.

    Use `scenario` to pick a predefined scenario, or set `scenario: "custom"` and
    provide a `custom` object with your own values (all in minutes).

    Available scenarios:
    - burnout: 2h+ focus without breaks → "Take a 5 min break"
    - distracted: 55min of Gaming → "Block Gaming for 25 min"
    - no_activity: nothing in 4h, below weekly average → trend insight
    - productive_streak: 5-day streak → "5 productive days in a row!"
    - low_week: today 45min vs usual 3h 20m → "Focus time is below your average"
    - peak_hour: current hour matches user's peak and focus is low → "Your best hour is now"
    - custom: specify all inputs yourself

    Example (burnout):
      curl -X POST http://localhost:8000/internal/simulate \\
        -H "Content-Type: application/json" \\
        -d '{"scenario": "burnout"}'

    Example (custom, cooldown test):
      curl -X POST http://localhost:8000/internal/simulate \\
        -H "Content-Type: application/json" \\
        -d '{
          "scenario": "custom",
          "custom": {
            "focus_4h_minutes": 150,
            "session_count_4h": 0,
            "avg_session_length_minutes": 60,
            "last_recommendation_type": "take_break",
            "last_recommendation_minutes_ago": 10
          }
        }'
    """
    return run_simulation(req)


@app.get("/internal/user-profile/{user_id}")
def user_profile(user_id: str):
    """
    Show the full recommendation context and what would be recommended next for a real user.

    Useful for debugging why a user is or isn't getting specific recommendations.
    Requires ClickHouse and PostgreSQL to be available.

    Example:
      curl http://localhost:8000/internal/user-profile/YOUR_USER_UUID
    """
    try:
        uid = UUID(user_id)
    except ValueError:
        raise HTTPException(status_code=400, detail="Invalid user_id — must be a UUID")

    from app.algorithm import COOLDOWN_SECONDS, build_recommendation, build_user_context

    ctx = build_user_context(uid)
    if not ctx:
        return {
            "user_id": user_id,
            "error": "No activity found for this user in the last 4h",
            "hint": "Make sure the user has synced usage or session data recently.",
        }

    rec, rule = build_recommendation(ctx)

    # Compute remaining cooldown for the last notification type
    cooldown_remaining_minutes: int | None = None
    if ctx.last_action_type and ctx.last_notification_seconds_ago is not None:
        cooldown_secs = COOLDOWN_SECONDS.get(ctx.last_action_type, 0)
        remaining = cooldown_secs - ctx.last_notification_seconds_ago
        if remaining > 0:
            cooldown_remaining_minutes = remaining // 60

    return {
        "user_id": user_id,
        "baseline": {
            "avg_daily_focus_minutes": ctx.baseline.avg_daily_focus_seconds // 60 if ctx.baseline else None,
            "avg_session_length_minutes": ctx.baseline.avg_session_length_seconds // 60 if ctx.baseline else None,
            "avg_daily_distraction_minutes": ctx.baseline.avg_daily_distraction_seconds // 60 if ctx.baseline else None,
            "peak_hour": ctx.baseline.peak_hour if ctx.baseline else None,
            "active_days": ctx.baseline.active_days if ctx.baseline else 0,
            "personalized": ctx.baseline is not None,
        },
        "current_window": {
            "4h": {
                "focus_minutes": ctx.focus_4h_seconds // 60,
                "distraction_minutes": ctx.distraction_4h_seconds // 60,
                "session_count": ctx.session_count_4h,
                "top_distraction_category": ctx.top_distraction_category,
            },
            "24h": {
                "focus_minutes": ctx.focus_24h_seconds // 60,
                "distraction_minutes": ctx.distraction_24h_seconds // 60,
            },
            "7d": {
                "focus_minutes": ctx.focus_7d_seconds // 60,
            },
        },
        "streak_days": ctx.streak_days,
        "hour_of_day": ctx.hour_of_day,
        "last_notification": {
            "action_type": ctx.last_action_type,
            "seconds_ago": ctx.last_notification_seconds_ago,
            "cooldown_remaining_minutes": cooldown_remaining_minutes,
        },
        "next_recommendation": {
            "title": rec.get("title") if rec else None,
            "rule_triggered": rule,
            "blocked_by_cooldown": rec is None and ctx.last_action_type is not None,
            "full": rec,
        },
    }
