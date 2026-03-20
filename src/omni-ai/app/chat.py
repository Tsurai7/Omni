"""
AI Coach chat — Google Gemini integration, context builder, conversation starters, rate limiter.
"""
from __future__ import annotations

import json
import logging
import os
import time
from collections import defaultdict
from datetime import datetime
from typing import Generator
from uuid import UUID

import google.generativeai as genai

from app.algorithm import (
    STREAK_MILESTONES,
    build_user_context,
    compute_focus_score,
    get_user_baseline,
)
from app.db import get_pg_engine

logger = logging.getLogger(__name__)

try:
    from google.api_core import exceptions as google_api_exceptions
except ImportError:
    google_api_exceptions = None  # type: ignore[misc, assignment]

_DEFAULT_GEMINI_PRIMARY = "gemini-2.5-flash-lite"
_DEFAULT_GEMINI_FALLBACKS = (
    "gemini-2.5-flash",
    "gemini-2.0-flash",
    "gemini-2.0-flash-lite",
)


def _gemini_api_key() -> str:
    return (
        (os.getenv("GEMINI_API_KEY") or "").strip()
        or (os.getenv("GOOGLE_API_KEY") or "").strip()
    )


def _gemini_model_chain() -> list[str]:
    """
    Ordered model ids: primary (GEMINI_MODEL or default) then fallbacks.
    GEMINI_MODEL_FALLBACKS: unset → built-in list; empty string → no fallbacks; else comma-separated.
    """
    primary = (os.getenv("GEMINI_MODEL") or "").strip() or _DEFAULT_GEMINI_PRIMARY
    fb_env = os.getenv("GEMINI_MODEL_FALLBACKS")
    if fb_env is None:
        fallbacks = list(_DEFAULT_GEMINI_FALLBACKS)
    elif not fb_env.strip():
        fallbacks = []
    else:
        fallbacks = [x.strip() for x in fb_env.split(",") if x.strip()]

    seen: set[str] = set()
    chain: list[str] = []
    for m in [primary, *fallbacks]:
        if m not in seen:
            seen.add(m)
            chain.append(m)
    return chain


def _should_fallback_to_next_model(exc: BaseException) -> bool:
    """True if trying another model in the chain might succeed."""
    if google_api_exceptions is not None and isinstance(
        exc,
        (
            google_api_exceptions.Unauthenticated,
            google_api_exceptions.PermissionDenied,
            google_api_exceptions.Unauthorized,
            google_api_exceptions.Forbidden,
        ),
    ):
        return False

    if google_api_exceptions is not None and isinstance(
        exc,
        (
            google_api_exceptions.NotFound,
            google_api_exceptions.ResourceExhausted,
            google_api_exceptions.TooManyRequests,
        ),
    ):
        return True

    raw = str(exc)
    low = raw.lower()
    if "401" in raw or "403" in raw:
        if any(
            x in low
            for x in (
                "unauthorized",
                "permission denied",
                "forbidden",
                "invalid api key",
                "api key not valid",
            )
        ):
            return False

    if (
        "429" in raw
        or "quota" in low
        or "resource exhausted" in low
        or "rate limit" in low
        or "exceeded your current quota" in low
    ):
        return True
    if "404" in raw or "not found" in low or "is not supported for generatecontent" in low:
        return True
    return False


def _gemini_all_models_failed_message(chain: list[str]) -> str:
    listed = ", ".join(chain)
    return (
        f"All configured Gemini models failed or hit quota ({listed}). "
        "Wait and retry, reduce coach usage, adjust GEMINI_MODEL / GEMINI_MODEL_FALLBACKS, "
        "or enable billing in Google AI Studio. "
        "See https://aistudio.google.com/rate-limit"
    )


def _gemini_stream_error_message(exc: BaseException, *, model_id: str = "") -> str:
    """Short user-visible text; log full exception separately."""
    raw = str(exc)
    low = raw.lower()
    model_hint = f" ({model_id})" if model_id else ""
    if "404" in raw or "not found" in low or "is not supported for generatecontent" in low:
        return (
            "Gemini model not available for this API key. In AI Studio, open the model list, "
            "pick a model that supports generateContent, set GEMINI_MODEL / GEMINI_MODEL_FALLBACKS, "
            "and redeploy."
        )
    if (
        "429" in raw
        or "quota" in low
        or "resource exhausted" in low
        or "rate limit" in low
        or "exceeded your current quota" in low
    ):
        return (
            f"Gemini quota or rate limit reached{model_hint}. "
            "Wait a few minutes for the window to reset, use the coach less often, "
            "or enable pay-as-you-go billing for this API key in Google AI / Cloud. "
            "If another model still has free quota, try GEMINI_MODEL or GEMINI_MODEL_FALLBACKS."
        )
    return "Sorry, I'm having trouble connecting right now. Try again in a moment."


# Rate limit: per user_id → list of timestamps
_rate_limit_store: dict[str, list[float]] = defaultdict(list)
RATE_LIMIT_HOURLY = 30
RATE_LIMIT_DAILY = 200


# ── Rate limiting ─────────────────────────────────────────────────────────────

def check_rate_limit(user_id: str) -> tuple[bool, str]:
    """
    Returns (allowed, reason). Cleans up stale timestamps inline.
    """
    now = time.time()
    timestamps = _rate_limit_store[user_id]

    # Remove entries older than 24h
    cutoff_day = now - 86400
    _rate_limit_store[user_id] = [t for t in timestamps if t > cutoff_day]
    timestamps = _rate_limit_store[user_id]

    daily_count = len(timestamps)
    hourly_count = sum(1 for t in timestamps if t > now - 3600)

    if hourly_count >= RATE_LIMIT_HOURLY:
        return False, f"Rate limit: {RATE_LIMIT_HOURLY} messages per hour"
    if daily_count >= RATE_LIMIT_DAILY:
        return False, f"Rate limit: {RATE_LIMIT_DAILY} messages per day"
    return True, ""


def record_message_sent(user_id: str) -> None:
    _rate_limit_store[user_id].append(time.time())


# ── Context builder ───────────────────────────────────────────────────────────

def _fmt_min(seconds: int) -> str:
    h = seconds // 3600
    m = (seconds % 3600) // 60
    if h > 0:
        return f"{h}h {m}m" if m > 0 else f"{h}h"
    return f"{m}m"


def _get_user_tasks(user_id: UUID) -> list[dict]:
    """Fetch up to 10 pending/in-progress tasks from PostgreSQL."""
    engine = get_pg_engine()
    if not engine:
        return []
    try:
        from sqlalchemy import text
        with engine.connect() as conn:
            rows = conn.execute(
                text("""
                SELECT title, status
                FROM tasks
                WHERE user_id = :user_id AND status != 'cancelled'
                ORDER BY
                    CASE status WHEN 'pending' THEN 0 WHEN 'done' THEN 1 ELSE 2 END,
                    created_at DESC
                LIMIT 10
                """),
                {"user_id": str(user_id)},
            ).fetchall()
        return [{"title": r[0], "status": r[1]} for r in rows]
    except Exception as e:
        logger.warning("_get_user_tasks failed: %s", e)
        return []


def _get_recent_sessions(user_id: UUID) -> list[dict]:
    """Fetch the last 5 focus sessions from PostgreSQL."""
    engine = get_pg_engine()
    if not engine:
        return []
    try:
        from sqlalchemy import text
        with engine.connect() as conn:
            rows = conn.execute(
                text("""
                SELECT name, duration_seconds, started_at, reflection_note
                FROM sessions
                WHERE user_id = :user_id
                ORDER BY started_at DESC
                LIMIT 5
                """),
                {"user_id": str(user_id)},
            ).fetchall()
        return [
            {
                "name": r[0],
                "duration_min": (r[1] or 0) // 60,
                "date": r[2].strftime("%a %b %d") if r[2] else "",
                "reflection": r[3] or "",
            }
            for r in rows
        ]
    except Exception as e:
        logger.warning("_get_recent_sessions failed: %s", e)
        return []


def _detect_milestones(streak: int, score: int) -> list[str]:
    """Return any milestone strings worth celebrating in the system prompt."""
    milestones = []
    if streak in STREAK_MILESTONES:
        milestones.append(f"The user just hit a {streak}-day streak milestone!")
    if score == 100:
        milestones.append("The user achieved a perfect focus score of 100 today!")
    elif score >= 90:
        milestones.append(f"The user has an exceptional focus score of {score} today!")
    return milestones


def build_chat_context(user_id: UUID) -> str:
    """
    Assemble a rich system prompt for the AI coach, injecting live user data.
    """
    hour = datetime.now().hour
    if hour < 12:
        time_of_day = "morning"
    elif hour < 17:
        time_of_day = "afternoon"
    else:
        time_of_day = "evening"

    # Gather data (best-effort — graceful on failures)
    ctx = build_user_context(user_id)
    score_result = compute_focus_score(user_id)
    tasks = _get_user_tasks(user_id)
    sessions = _get_recent_sessions(user_id)
    baseline = get_user_baseline(user_id, days=30)

    streak = score_result.streak_days
    score = score_result.score
    trend = score_result.trend
    focus_min = score_result.focus_minutes_today
    sessions_today = score_result.sessions_today

    milestones = _detect_milestones(streak, score)

    # Build context sections
    current_state = f"""CURRENT STATE (live data — reference this, never fabricate):
- Focus Score: {score}/100 (trend: {trend})
- Today: {focus_min}m of focus, {sessions_today} session(s) completed
- Streak: {streak} consecutive productive days
- Time of day: {time_of_day}"""

    if ctx:
        distraction_min = ctx.distraction_4h_seconds // 60
        top_distraction = ctx.top_distraction_category or "none"
        current_state += f"""
- Last 4h: {ctx.focus_4h_seconds // 60}m focus, {distraction_min}m distraction
- Top distraction: {top_distraction}"""

    if baseline:
        current_state += f"""

BEHAVIORAL BASELINE (30-day average):
- Avg daily focus: {_fmt_min(baseline.avg_daily_focus_seconds)}
- Avg daily distraction: {_fmt_min(baseline.avg_daily_distraction_seconds)}
- Avg session length: {_fmt_min(baseline.avg_session_length_seconds)}
- Peak productive hour: {baseline.peak_hour:02d}:00 local time
- Active days tracked: {baseline.active_days}"""

    tasks_section = ""
    if tasks:
        task_lines = "\n".join(
            f"  - [{t['status'].upper()}] {t['title']}" for t in tasks
        )
        tasks_section = f"\nUSER'S TASKS:\n{task_lines}"
    else:
        tasks_section = "\nUSER'S TASKS: No tasks found."

    sessions_section = ""
    if sessions:
        session_lines = "\n".join(
            f"  - {s['name']} ({s['duration_min']}m) on {s['date']}"
            + (f" — reflection: \"{s['reflection']}\"" if s["reflection"] else "")
            for s in sessions
        )
        sessions_section = f"\nRECENT SESSIONS:\n{session_lines}"

    milestones_section = ""
    if milestones:
        milestones_section = "\nMILESTONES TO CELEBRATE:\n" + "\n".join(f"  - {m}" for m in milestones)

    return f"""You are Omni, an AI productivity coach built into the Omni focus-tracking app.

PERSONALITY:
- Warm, concise, and genuinely curious about the user's work
- Reference actual user data naturally — never make up numbers
- Celebrate wins authentically but briefly
- Frame setbacks constructively: "X happened — here's one way forward"
- Ask one reflective question when it adds value; don't interrogate
- Suggest concrete actions when relevant; format them clearly
- Keep responses under 150 words unless the user asks for more detail
- Use short punchy sentences. No bullet dumps. Conversational tone.
- You can mention: starting a session, creating a task, taking a break, viewing stats

{current_state}
{tasks_section}
{sessions_section}
{milestones_section}

When the user asks to take an action (start session, create task, take a break), end your reply with a JSON block on its own line:
ACTION:{{"type":"start_session","label":"Start 25min focus"}}
ACTION:{{"type":"create_task","label":"Create task","title":"<suggested title>"}}
ACTION:{{"type":"take_break","label":"Take a 5min break"}}
ACTION:{{"type":"view_stats","label":"View usage stats"}}
Only include an ACTION line when truly appropriate — don't force it."""


# ── Conversation starters ─────────────────────────────────────────────────────

def generate_starters(user_id: UUID) -> list[dict]:
    """
    Produce 3-4 context-aware conversation starter chips.
    """
    try:
        score_result = compute_focus_score(user_id)
        streak = score_result.streak_days
        score = score_result.score
        focus_min = score_result.focus_minutes_today
        sessions_today = score_result.sessions_today
        trend = score_result.trend
    except Exception:
        score = 0
        streak = 0
        focus_min = 0
        sessions_today = 0
        trend = "flat"

    try:
        tasks = _get_user_tasks(user_id)
        pending_count = sum(1 for t in tasks if t["status"] == "pending")
    except Exception:
        pending_count = 0

    hour = datetime.now().hour
    starters = []

    # Time-of-day starters
    if hour < 12:
        if pending_count > 0:
            starters.append({"text": f"I have {pending_count} tasks — help me plan my day", "icon": "task"})
        else:
            starters.append({"text": "Help me plan my focus blocks for today", "icon": "focus"})
    elif hour >= 17:
        if focus_min > 0:
            starters.append({"text": f"I logged {focus_min}m of focus today — how did I do?", "icon": "focus"})
        else:
            starters.append({"text": "Let's do an evening reflection on my day", "icon": "insight"})

    # Streak starters
    if streak >= 3:
        if streak in STREAK_MILESTONES:
            starters.append({"text": f"I just hit a {streak}-day streak!", "icon": "streak"})
        else:
            starters.append({"text": f"I'm on a {streak}-day streak — what's working?", "icon": "streak"})
    elif streak == 0 and focus_min == 0 and hour >= 10:
        starters.append({"text": "I haven't focused yet today — help me get started", "icon": "focus"})

    # Score-based starters
    if score < 40 and (focus_min > 0 or sessions_today > 0):
        starters.append({"text": f"My focus score is {score} — how can I turn it around?", "icon": "insight"})
    elif score >= 80 and trend == "up":
        starters.append({"text": f"Focus score {score} and trending up — how do I keep this going?", "icon": "insight"})

    # Near milestone
    if streak > 0 and (streak + 1) in STREAK_MILESTONES:
        starters.append({"text": f"One more day for a {streak + 1}-day streak — any tips?", "icon": "streak"})

    # Always-available fallback
    starters.append({"text": "How's my productivity looking this week?", "icon": "insight"})

    # Deduplicate and limit to 4
    seen = set()
    unique = []
    for s in starters:
        if s["text"] not in seen:
            seen.add(s["text"])
            unique.append(s)
    return unique[:4]


# ── Streaming chat ────────────────────────────────────────────────────────────

def _history_to_gemini_turns(history: list[dict]) -> list[dict]:
    """Map stored user/assistant messages to Gemini chat history format."""
    turns: list[dict] = []
    for msg in history:
        role = msg.get("role")
        content = (msg.get("content") or "").strip()
        if not content:
            continue
        if role == "user":
            turns.append({"role": "user", "parts": [content]})
        elif role == "assistant":
            turns.append({"role": "model", "parts": [content]})
    return turns


def _stream_single_model(
    model_name: str,
    system_prompt: str,
    gem_history: list[dict],
    user_message: str,
) -> Generator[str, None, None]:
    """Yields SSE `data: {...}\\n\\n` lines for text deltas only. Raises on failure."""
    model = genai.GenerativeModel(
        model_name,
        system_instruction=system_prompt,
    )
    generation_config = genai.GenerationConfig(
        max_output_tokens=1024,
        temperature=0.7,
    )
    chat = model.start_chat(history=gem_history)
    response = chat.send_message(
        user_message,
        stream=True,
        generation_config=generation_config,
    )
    for chunk in response:
        try:
            text = chunk.text or ""
        except ValueError:
            # No text in this chunk (e.g. safety filter, finish reason only)
            continue
        if text:
            payload = json.dumps({"delta": text})
            yield f"data: {payload}\n\n"


def stream_chat_response(
    system_prompt: str,
    history: list[dict],
    user_message: str,
) -> Generator[str, None, None]:
    """
    Stream a Gemini completion. Yields SSE-formatted lines.
    Each line: "data: {json}\\n\\n"
    Final line: "data: {done: true}\\n\\n"
    Tries GEMINI_MODEL (or default) then GEMINI_MODEL_FALLBACKS on quota / unavailable model.
    """
    if not _gemini_api_key():
        yield 'data: {"delta": "Gemini API key not configured (set GEMINI_API_KEY or GOOGLE_API_KEY).", "error": true}\n\n'
        yield 'data: {"done": true}\n\n'
        return

    genai.configure(api_key=_gemini_api_key())
    gem_history = _history_to_gemini_turns(history)
    chain = _gemini_model_chain()

    last_error: BaseException | None = None
    for model_name in chain:
        stream = _stream_single_model(
            model_name, system_prompt, gem_history, user_message
        )
        sent = False
        try:
            for line in stream:
                sent = True
                yield line
            break
        except Exception as e:
            last_error = e
            if sent:
                logger.error("Gemini mid-stream error (%s): %s", model_name, e)
                err_payload = json.dumps(
                    {
                        "delta": _gemini_stream_error_message(
                            e, model_id=model_name
                        ),
                        "error": True,
                    }
                )
                yield f"data: {err_payload}\n\n"
                break
            if _should_fallback_to_next_model(e):
                logger.warning(
                    "Gemini model %s failed (%s), trying next in chain",
                    model_name,
                    e,
                )
                continue
            logger.error("Gemini stream error (%s): %s", model_name, e)
            err_payload = json.dumps(
                {
                    "delta": _gemini_stream_error_message(
                        e, model_id=model_name
                    ),
                    "error": True,
                }
            )
            yield f"data: {err_payload}\n\n"
            break
    else:
        if last_error is not None:
            logger.error(
                "Gemini: all models in chain failed; last error: %s", last_error
            )
            err_payload = json.dumps(
                {
                    "delta": _gemini_all_models_failed_message(chain),
                    "error": True,
                }
            )
            yield f"data: {err_payload}\n\n"

    yield 'data: {"done": true}\n\n'


def collect_full_response(
    system_prompt: str,
    history: list[dict],
    user_message: str,
) -> str:
    """
    Non-streaming call — used to get the full response text for storage after streaming.
    Extracts all delta tokens from the generator.
    """
    parts = []
    for line in stream_chat_response(system_prompt, history, user_message):
        if line.startswith("data: "):
            try:
                data = json.loads(line[6:])
                if "delta" in data and not data.get("error"):
                    parts.append(data["delta"])
            except json.JSONDecodeError:
                pass
    return "".join(parts)


def extract_action_from_response(content: str) -> dict | None:
    """
    Parse an ACTION:{...} line from the AI response if present.
    Returns the action dict or None.
    """
    for line in content.splitlines():
        line = line.strip()
        if line.startswith("ACTION:"):
            try:
                return json.loads(line[7:])
            except json.JSONDecodeError:
                pass
    return None


def strip_action_line(content: str) -> str:
    """Remove ACTION:{...} lines from visible response content."""
    lines = [l for l in content.splitlines() if not l.strip().startswith("ACTION:")]
    return "\n".join(lines).strip()
