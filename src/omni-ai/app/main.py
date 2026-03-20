"""
Omni AI microservice — health, recommendations, and AI coach chat.
"""
import sys
from contextlib import asynccontextmanager
from uuid import UUID

from fastapi import FastAPI, HTTPException
from fastapi.responses import StreamingResponse
from pydantic import BaseModel

from app.algorithm import compute_focus_score, generate_weekly_digest
from app.chat import (
    build_chat_context,
    check_rate_limit,
    collect_full_response,
    extract_action_from_response,
    generate_starters,
    record_message_sent,
    stream_chat_response,
    strip_action_line,
)
from app.chat_db import (
    conversation_belongs_to_user,
    create_conversation,
    delete_conversation,
    get_conversations,
    get_messages,
    insert_message,
)
from app.db import close_clients, get_clickhouse_client, get_pg_engine, init_clients
from app.scheduler import run_recommendation_job_once, run_weekly_digest_job, start_scheduler, stop_scheduler
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


@app.get("/api/ai/focus-score/{user_id}")
def focus_score(user_id: str):
    """
    Return the 0-100 daily focus score for a user with per-dimension breakdown.
    Called by the client via the API gateway.
    """
    try:
        uid = UUID(user_id)
    except ValueError:
        raise HTTPException(status_code=400, detail="Invalid user_id — must be a UUID")
    result = compute_focus_score(uid)
    return {
        "score": result.score,
        "breakdown": {
            "focus_ratio": result.focus_ratio,
            "session_completion": result.session_completion,
            "distraction_penalty": result.distraction_penalty,
            "consistency_bonus": result.consistency_bonus,
        },
        "trend": result.trend,
        "focus_minutes_today": result.focus_minutes_today,
        "sessions_today": result.sessions_today,
        "streak_days": result.streak_days,
    }


# ── Chat endpoints ────────────────────────────────────────────────────────────

class SendMessageRequest(BaseModel):
    conversation_id: str | None = None
    content: str


def _parse_uid(user_id: str) -> UUID:
    try:
        return UUID(user_id)
    except ValueError:
        raise HTTPException(status_code=400, detail="Invalid user_id — must be a UUID")


@app.get("/api/ai/chat/{user_id}/starters")
def chat_starters(user_id: str):
    """
    Return 3-4 context-aware conversation starter chips for the given user.
    """
    uid = _parse_uid(user_id)
    starters = generate_starters(uid)
    return {"starters": starters}


@app.get("/api/ai/chat/{user_id}/conversations")
def list_conversations(user_id: str):
    """
    Return the user's most recent conversations.
    """
    uid = _parse_uid(user_id)
    convs = get_conversations(uid, limit=10)
    return {"conversations": convs}


@app.get("/api/ai/chat/{user_id}/conversations/{conversation_id}/messages")
def get_conversation_messages(user_id: str, conversation_id: str, limit: int = 20):
    """
    Return message history for a conversation (oldest-first for display).
    """
    uid = _parse_uid(user_id)
    if not conversation_belongs_to_user(conversation_id, uid):
        raise HTTPException(status_code=404, detail="Conversation not found")
    msgs = get_messages(conversation_id, limit=limit)
    # Return newest-last (already ordered by created_at ASC from DB)
    return {"messages": msgs}


@app.post("/api/ai/chat/{user_id}/messages")
def send_message(user_id: str, req: SendMessageRequest):
    """
    Send a user message and stream the AI coach response via Server-Sent Events.

    If conversation_id is null a new conversation is created automatically.
    SSE format:
      data: {"delta": "token", "conversation_id": "uuid"}
      ...
      data: {"done": true, "conversation_id": "uuid"}
    """
    uid = _parse_uid(user_id)

    if not req.content or not req.content.strip():
        raise HTTPException(status_code=400, detail="Message content cannot be empty")

    # Rate limiting
    allowed, reason = check_rate_limit(user_id)
    if not allowed:
        raise HTTPException(status_code=429, detail=reason)

    # Resolve or create conversation
    conv_id = req.conversation_id
    if conv_id:
        if not conversation_belongs_to_user(conv_id, uid):
            raise HTTPException(status_code=404, detail="Conversation not found")
    else:
        # Auto-title from first ~40 chars of user message
        title = req.content.strip()[:40] + ("…" if len(req.content.strip()) > 40 else "")
        conv_id = create_conversation(uid, title)
        if not conv_id:
            raise HTTPException(status_code=500, detail="Failed to create conversation")

    # Persist user message
    insert_message(conv_id, uid, "user", req.content.strip())
    record_message_sent(user_id)

    # Load history (last 20 messages for LLM context)
    history = get_messages(conv_id, limit=20)
    # Exclude the message we just inserted (it'll be appended by stream_chat_response)
    history = [m for m in history if not (m["role"] == "user" and m["content"] == req.content.strip())][-19:]

    # Build system prompt with live user context
    system_prompt = build_chat_context(uid)

    # Buffer the full response to persist it after streaming
    full_response_buffer: list[str] = []

    def event_stream():
        import json as _json

        full_text_parts: list[str] = []
        for sse_line in stream_chat_response(system_prompt, history, req.content.strip()):
            # Collect full text for post-stream storage
            if sse_line.startswith("data: "):
                try:
                    data = _json.loads(sse_line[6:])
                    if "delta" in data and not data.get("error"):
                        full_text_parts.append(data["delta"])
                except Exception:
                    pass

            if sse_line.strip() == 'data: {"done": true}':
                # Persist the full assistant response
                full_text = "".join(full_text_parts)
                visible_text = strip_action_line(full_text)
                action = extract_action_from_response(full_text)
                metadata = {"actions": [action]} if action else None
                insert_message(conv_id, uid, "assistant", visible_text, metadata)
                # Send final event with conversation_id
                yield f'data: {{"done": true, "conversation_id": "{conv_id}"}}\n\n'
            else:
                # Inject conversation_id into delta events
                if sse_line.startswith("data: "):
                    try:
                        import json as _j
                        data = _j.loads(sse_line[6:])
                        data["conversation_id"] = conv_id
                        yield f"data: {_j.dumps(data)}\n\n"
                        continue
                    except Exception:
                        pass
                yield sse_line

    return StreamingResponse(
        event_stream(),
        media_type="text/event-stream",
        headers={
            "Cache-Control": "no-cache",
            "X-Accel-Buffering": "no",
        },
    )


@app.delete("/api/ai/chat/{user_id}/conversations/{conversation_id}")
def remove_conversation(user_id: str, conversation_id: str):
    """
    Soft-delete a conversation.
    """
    uid = _parse_uid(user_id)
    if not conversation_belongs_to_user(conversation_id, uid):
        raise HTTPException(status_code=404, detail="Conversation not found")
    delete_conversation(uid, conversation_id)
    return {"deleted": True}


@app.post("/internal/run-weekly-digest")
def trigger_weekly_digest():
    """
    Run the weekly digest job immediately (for testing).
    Sends digest notifications to all users with activity in the last 7 days.
    """
    return run_weekly_digest_job()


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
