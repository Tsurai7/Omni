"""
Anomaly detection and data-driven recommendation logic.
Queries ClickHouse for telemetry aggregates and produces one recommendation per user per run.
"""
from __future__ import annotations

import logging
from dataclasses import dataclass
from typing import Any
from uuid import UUID

from app.db import get_clickhouse_client

logger = logging.getLogger(__name__)

# Align with client: focus vs distraction categories
FOCUS_CATEGORIES = ("Coding", "Productivity")
DISTRACTION_CATEGORIES = ("Gaming", "Chilling")
FOCUS_SQL = "category IN ('Coding', 'Productivity')"
DISTRACTION_SQL = "category IN ('Gaming', 'Chilling')"

# Thresholds (seconds)
FOCUS_BURNOUT_THRESHOLD = 2 * 3600  # 2h sustained focus → suggest break
MIN_BREAK_SESSIONS = 1  # expect at least one short "break" in 4h
HIGH_DISTRACTION_MIN = 30 * 60  # 30 min distraction → suggest block
RECOMMEND_DURATION_MIN = 25


@dataclass
class UserAggregates:
    """Per-user telemetry aggregates for a time window."""

    focus_seconds: int
    distraction_seconds: int
    session_count: int
    total_session_seconds: int
    max_session_seconds: int
    top_distraction_category: str | None


def get_active_user_ids(hours: int = 24) -> list[UUID]:
    """Return distinct user_id with at least one event in the last `hours` hours."""
    client = get_clickhouse_client()
    if not client:
        return []
    try:
        q = f"""
        SELECT DISTINCT user_id
        FROM omni_analytics.telemetry_events
        WHERE at >= now() - toIntervalHour({hours})
        """
        r = client.query(q)
        out: list[UUID] = []
        for row in r.result_set or []:
            try:
                out.append(UUID(str(row[0])))
            except (TypeError, ValueError):
                continue
        return out
    except Exception as e:
        logger.warning("get_active_user_ids failed: %s", e)
        return []


def get_user_aggregates(user_id: UUID, hours: int) -> UserAggregates | None:
    """
    Aggregate usage (focus vs distraction) and session stats for one user over the last `hours`.
    """
    client = get_clickhouse_client()
    if not client:
        return None
    try:
        # Usage: focus and distraction seconds; top distraction category
        usage_q = f"""
        SELECT
            sumIf(duration_seconds, {FOCUS_SQL}) AS focus_seconds,
            sumIf(duration_seconds, {DISTRACTION_SQL}) AS distraction_seconds,
            argMax(category, duration_seconds) FILTER (WHERE {DISTRACTION_SQL}) AS top_distraction
        FROM omni_analytics.telemetry_events
        WHERE event_type = 'usage'
          AND user_id = %(user_id)s
          AND at >= now() - toIntervalHour(%(hours)s)
        """
        uid = str(user_id)
        usage_r = client.query(
            usage_q.replace("%(user_id)s", f"'{uid}'").replace("%(hours)s", str(hours)),
        )
        focus_seconds = 0
        distraction_seconds = 0
        top_distraction_category: str | None = None
        if usage_r.result_set and len(usage_r.result_set) > 0:
            row = usage_r.result_set[0]
            focus_seconds = int(row[0] or 0)
            distraction_seconds = int(row[1] or 0)
            if row[2]:
                top_distraction_category = str(row[2]).strip() or None

        # Sessions: count, total duration, max duration
        session_q = """
        SELECT
            count() AS session_count,
            sum(duration_seconds) AS total_seconds,
            max(duration_seconds) AS max_seconds
        FROM omni_analytics.telemetry_events
        WHERE event_type = 'session'
          AND user_id = %(user_id)s
          AND at >= now() - toIntervalHour(%(hours)s)
        """
        session_r = client.query(
            session_q.replace("%(user_id)s", f"'{uid}'").replace("%(hours)s", str(hours)),
        )
        session_count = 0
        total_session_seconds = 0
        max_session_seconds = 0
        if session_r.result_set and len(session_r.result_set) > 0:
            row = session_r.result_set[0]
            session_count = int(row[0] or 0)
            total_session_seconds = int(row[1] or 0)
            max_session_seconds = int(row[2] or 0)

        return UserAggregates(
            focus_seconds=focus_seconds,
            distraction_seconds=distraction_seconds,
            session_count=session_count,
            total_session_seconds=total_session_seconds,
            max_session_seconds=max_session_seconds,
            top_distraction_category=top_distraction_category,
        )
    except Exception as e:
        logger.warning("get_user_aggregates failed for %s: %s", user_id, e)
        return None


def build_recommendation(
    agg_4h: UserAggregates | None,
    agg_24h: UserAggregates | None,
    agg_7d: UserAggregates | None,
) -> dict[str, Any] | None:
    """
    Heuristic-based recommendation: one primary suggestion per run.
    Priority: burnout break > block distraction > start focus > trend insight.
    """
    if not agg_4h:
        return None

    # 1. Sustained focus without breaks (burnout risk)
    if (
        agg_4h.focus_seconds >= FOCUS_BURNOUT_THRESHOLD
        and agg_4h.session_count < MIN_BREAK_SESSIONS
    ):
        return {
            "type": "recommendation",
            "title": "Take a 5 min break",
            "body": "You've been focused for over 2 hours. A short break can help sustain focus.",
            "action_type": "take_break",
            "action_payload": {"duration_minutes": 5},
        }

    # 2. High distraction in last 4h → suggest blocking top category
    if agg_4h.distraction_seconds >= HIGH_DISTRACTION_MIN and agg_4h.top_distraction_category:
        cat = agg_4h.top_distraction_category
        return {
            "type": "recommendation",
            "title": f"Block {cat} for {RECOMMEND_DURATION_MIN} min",
            "body": f"Most distraction time was in {cat}. Try a short focus block.",
            "action_type": "block_category",
            "action_payload": {"category": cat, "duration_minutes": RECOMMEND_DURATION_MIN},
        }

    # 3. No focus in 4h but some activity → nudge to start
    if agg_4h.focus_seconds < 15 * 60 and (agg_4h.distraction_seconds > 0 or agg_4h.session_count > 0):
        return {
            "type": "recommendation",
            "title": "Start a 25 min focus session",
            "body": "No focus time in the last 4 hours. One deep work block can get you back on track.",
            "action_type": "start_session",
            "action_payload": {"duration_minutes": RECOMMEND_DURATION_MIN},
        }

    # 4. Trend: 7d vs previous 7d (simplified: compare 24h focus to 24h before)
    if agg_24h and agg_7d and agg_7d.focus_seconds > 0:
        # Rough: 7d includes last 24h; use 24h as "recent" vs (7d - 24h) as "earlier"
        recent = agg_24h.focus_seconds
        earlier = max(0, agg_7d.focus_seconds - recent)
        if earlier > 0 and recent < earlier * 0.8:
            return {
                "type": "insight",
                "title": "Focus time is down vs last week",
                "body": "Try one deep work block to build momentum.",
                "action_type": "start_session",
                "action_payload": {"duration_minutes": RECOMMEND_DURATION_MIN},
            }
        if earlier > 0 and recent > earlier * 1.2:
            return {
                "type": "insight",
                "title": "Focus time is up vs last week",
                "body": "Keep the momentum with another block when ready.",
                "action_type": "none",
                "action_payload": None,
            }

    # 5. Default: gentle nudge if no focus today (from 4h)
    if agg_4h.focus_seconds < 30 * 60:
        return {
            "type": "recommendation",
            "title": "Start a 25 min focus session",
            "body": "A short focus block can help you get into flow.",
            "action_type": "start_session",
            "action_payload": {"duration_minutes": RECOMMEND_DURATION_MIN},
        }

    return None


def run_analysis_for_user(user_id: UUID) -> dict[str, Any] | None:
    """
    Load 4h, 24h, and 7d aggregates for the user and return one recommendation.
    """
    agg_4h = get_user_aggregates(user_id, 4)
    agg_24h = get_user_aggregates(user_id, 24)
    agg_7d = get_user_aggregates(user_id, 24 * 7)
    return build_recommendation(agg_4h, agg_24h, agg_7d)
