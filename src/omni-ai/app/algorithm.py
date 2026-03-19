"""
Anomaly detection and data-driven recommendation logic.
Queries ClickHouse for telemetry aggregates and produces one recommendation per user per run.
"""
from __future__ import annotations

import logging
from dataclasses import dataclass
from datetime import date, datetime, timedelta
from typing import Any
from uuid import UUID

from app.db import get_clickhouse_client

logger = logging.getLogger(__name__)

# Align with client: focus vs distraction categories
FOCUS_CATEGORIES = ("Coding", "Productivity")
DISTRACTION_CATEGORIES = ("Gaming", "Chilling")
FOCUS_SQL = "category IN ('Coding', 'Productivity')"
DISTRACTION_SQL = "category IN ('Gaming', 'Chilling')"

# Fixed fallback thresholds (used when no personalized baseline exists)
BURNOUT_FIXED_SECONDS = 2 * 3600        # 2h sustained focus without break
LOW_FOCUS_FIXED_SECONDS = 15 * 60       # 15min is "almost nothing"
HIGH_DISTRACTION_FIXED_SECONDS = 30 * 60  # 30min distraction → suggest block

# Personalized multipliers
BURNOUT_RATIO = 1.5     # focus_4h > 1.5× avg_session_length → burnout risk
LOW_FOCUS_RATIO = 0.3   # focus_4h < 30% of typical 4h pace → nudge
HIGH_DISTRACTION_RATIO = 0.5  # distraction > 50% of typical daily rate per 4h

# Streak minimum: 30 min focus counts as a "productive day"
STREAK_MIN_DAILY_FOCUS_SECONDS = 30 * 60

RECOMMEND_DURATION_MIN = 25

# Cooldown per action type — don't repeat same action within this window
COOLDOWN_SECONDS: dict[str, int] = {
    "take_break":     30 * 60,   # 30 min
    "start_session":  60 * 60,   # 1h
    "block_category": 2 * 3600,  # 2h
    "none":           4 * 3600,  # 4h for insights
}


@dataclass
class UserAggregates:
    """Per-user telemetry aggregates for a time window."""
    focus_seconds: int
    distraction_seconds: int
    session_count: int
    total_session_seconds: int
    max_session_seconds: int
    top_distraction_category: str | None


@dataclass
class UserBaseline:
    """30-day behavioral profile for a user."""
    avg_daily_focus_seconds: int = 0
    avg_session_length_seconds: int = 0
    avg_daily_distraction_seconds: int = 0
    peak_hour: int | None = None  # hour 0–23 when user is most productive
    active_days: int = 0


@dataclass
class RecommendationContext:
    """All inputs needed by build_recommendation — works for both real and simulated data."""
    # 4h window
    focus_4h_seconds: int = 0
    distraction_4h_seconds: int = 0
    session_count_4h: int = 0
    top_distraction_category: str | None = None
    # 24h window
    focus_24h_seconds: int = 0
    distraction_24h_seconds: int = 0
    # 7d window
    focus_7d_seconds: int = 0
    # personalized baseline (None → use fixed thresholds)
    baseline: UserBaseline | None = None
    # context
    streak_days: int = 0
    hour_of_day: int = 12
    # cooldown tracking
    last_action_type: str | None = None
    last_notification_seconds_ago: int | None = None


# ── Helpers ──────────────────────────────────────────────────────────────────

def _fmt_duration(seconds: int) -> str:
    h = seconds // 3600
    m = (seconds % 3600) // 60
    if h > 0:
        return f"{h}h {m}m" if m > 0 else f"{h}h"
    return f"{m}m"


def _on_cooldown(action_type: str, ctx: RecommendationContext) -> bool:
    """Return True if this action_type was used recently and is still within its cooldown."""
    if ctx.last_action_type != action_type or ctx.last_notification_seconds_ago is None:
        return False
    return ctx.last_notification_seconds_ago < COOLDOWN_SECONDS.get(action_type, 0)


# ── Core recommendation logic (pure, no DB) ───────────────────────────────────

def build_recommendation(ctx: RecommendationContext) -> tuple[dict[str, Any] | None, str | None]:
    """
    Evaluate all rules in priority order. Returns (recommendation_dict, rule_name).
    Uses personalized baselines when available; falls back to fixed thresholds.
    """
    b = ctx.baseline

    # ── Rule 1: Burnout break ─────────────────────────────────────────────────
    burnout_threshold = (
        int(b.avg_session_length_seconds * BURNOUT_RATIO)
        if b and b.avg_session_length_seconds > 300  # ignore baselines < 5min
        else BURNOUT_FIXED_SECONDS
    )
    if ctx.focus_4h_seconds >= burnout_threshold and ctx.session_count_4h == 0:
        action = "take_break"
        if not _on_cooldown(action, ctx):
            focus_str = _fmt_duration(ctx.focus_4h_seconds)
            body = f"You've been in focus for {focus_str} without a break."
            if b and b.avg_session_length_seconds > 0:
                body += f" Your sessions usually run ~{_fmt_duration(b.avg_session_length_seconds)}."
            body += " A 5 min break helps sustain deep work."
            return {
                "type": "recommendation",
                "title": "Take a 5 min break",
                "body": body,
                "action_type": action,
                "action_payload": {"duration_minutes": 5},
            }, "sustained_focus_burnout"

    # ── Rule 2: Streak celebration ────────────────────────────────────────────
    if ctx.streak_days >= 3 and not _on_cooldown("none", ctx):
        return {
            "type": "insight",
            "title": f"{ctx.streak_days} productive days in a row!",
            "body": f"You've had solid focus for {ctx.streak_days} days straight. Keep the momentum.",
            "action_type": "none",
            "action_payload": None,
        }, "streak_celebration"

    # ── Rule 3: Peak hour nudge ───────────────────────────────────────────────
    if (
        b and b.peak_hour is not None
        and abs(ctx.hour_of_day - b.peak_hour) <= 1
        and ctx.focus_4h_seconds < 20 * 60
        and not _on_cooldown("start_session", ctx)
    ):
        return {
            "type": "recommendation",
            "title": "Your best hour is now",
            "body": f"You usually do your deepest work around {b.peak_hour:02d}:00. Start a session?",
            "action_type": "start_session",
            "action_payload": {"duration_minutes": RECOMMEND_DURATION_MIN},
        }, "peak_hour_nudge"

    # ── Rule 4: Block distraction ─────────────────────────────────────────────
    distraction_threshold = (
        int(max(20 * 60, (b.avg_daily_distraction_seconds / 4) * HIGH_DISTRACTION_RATIO))
        if b and b.avg_daily_distraction_seconds > 0
        else HIGH_DISTRACTION_FIXED_SECONDS
    )
    if ctx.distraction_4h_seconds >= distraction_threshold and ctx.top_distraction_category:
        action = "block_category"
        if not _on_cooldown(action, ctx):
            cat = ctx.top_distraction_category
            dist_str = _fmt_duration(ctx.distraction_4h_seconds)
            body = f"You've spent {dist_str} in {cat} in the last 4h."
            if b and b.avg_daily_distraction_seconds > 0:
                body += f" Your daily avg is ~{_fmt_duration(b.avg_daily_distraction_seconds)}."
            body += f" Try a {RECOMMEND_DURATION_MIN} min focus block."
            return {
                "type": "recommendation",
                "title": f"Block {cat} for {RECOMMEND_DURATION_MIN} min",
                "body": body,
                "action_type": action,
                "action_payload": {"category": cat, "duration_minutes": RECOMMEND_DURATION_MIN},
            }, "high_distraction"

    # ── Rule 5: No focus nudge ────────────────────────────────────────────────
    low_focus_threshold = (
        int(b.avg_daily_focus_seconds / 4 * LOW_FOCUS_RATIO)
        if b and b.avg_daily_focus_seconds > 0
        else LOW_FOCUS_FIXED_SECONDS
    )
    has_some_activity = ctx.distraction_4h_seconds > 0 or ctx.session_count_4h > 0
    if ctx.focus_4h_seconds <= low_focus_threshold and has_some_activity:
        action = "start_session"
        if not _on_cooldown(action, ctx):
            body = "No significant focus time in the last 4h."
            if b and b.avg_daily_focus_seconds > 0:
                body += f" You typically hit ~{_fmt_duration(b.avg_daily_focus_seconds)} of focus per day."
            body += " One deep work block can get you back on track."
            return {
                "type": "recommendation",
                "title": "Start a 25 min focus session",
                "body": body,
                "action_type": action,
                "action_payload": {"duration_minutes": RECOMMEND_DURATION_MIN},
            }, "no_focus_activity"

    # ── Rule 6: Focus down trend ──────────────────────────────────────────────
    if b and b.avg_daily_focus_seconds > 0:
        if ctx.focus_24h_seconds < b.avg_daily_focus_seconds * 0.7:
            action = "start_session"
            if not _on_cooldown(action, ctx):
                today_str = _fmt_duration(ctx.focus_24h_seconds)
                avg_str = _fmt_duration(b.avg_daily_focus_seconds)
                return {
                    "type": "insight",
                    "title": "Focus time is below your average",
                    "body": f"You've done {today_str} of focus today vs your usual ~{avg_str}. One block can turn it around.",
                    "action_type": action,
                    "action_payload": {"duration_minutes": RECOMMEND_DURATION_MIN},
                }, "focus_down_trend"
    elif ctx.focus_24h_seconds > 0 and ctx.focus_7d_seconds > 0:
        # Fallback without baseline: compare today to (7d − today) / 6
        earlier_avg = max(0, ctx.focus_7d_seconds - ctx.focus_24h_seconds) / 6
        if earlier_avg > 0 and ctx.focus_24h_seconds < earlier_avg * 0.8:
            action = "start_session"
            if not _on_cooldown(action, ctx):
                return {
                    "type": "insight",
                    "title": "Focus time is down vs last week",
                    "body": "Try one deep work block to build momentum.",
                    "action_type": action,
                    "action_payload": {"duration_minutes": RECOMMEND_DURATION_MIN},
                }, "focus_down_trend_7d"

    # ── Rule 7: Focus up trend (positive insight) ─────────────────────────────
    if b and b.avg_daily_focus_seconds > 0 and ctx.focus_24h_seconds > b.avg_daily_focus_seconds * 1.3:
        if not _on_cooldown("none", ctx):
            today_str = _fmt_duration(ctx.focus_24h_seconds)
            avg_str = _fmt_duration(b.avg_daily_focus_seconds)
            return {
                "type": "insight",
                "title": "Great focus day!",
                "body": f"You've done {today_str} of focus today — above your usual ~{avg_str}. Keep going.",
                "action_type": "none",
                "action_payload": None,
            }, "focus_up_trend"
    elif ctx.focus_24h_seconds > 0 and ctx.focus_7d_seconds > 0:
        earlier_avg = max(0, ctx.focus_7d_seconds - ctx.focus_24h_seconds) / 6
        if earlier_avg > 0 and ctx.focus_24h_seconds > earlier_avg * 1.2:
            if not _on_cooldown("none", ctx):
                return {
                    "type": "insight",
                    "title": "Focus time is up vs last week",
                    "body": "Keep the momentum with another block when ready.",
                    "action_type": "none",
                    "action_payload": None,
                }, "focus_up_trend_7d"

    # ── Rule 8: Default nudge ─────────────────────────────────────────────────
    if ctx.focus_4h_seconds < 30 * 60:
        action = "start_session"
        if not _on_cooldown(action, ctx):
            return {
                "type": "recommendation",
                "title": "Start a 25 min focus session",
                "body": "A short focus block can help you get into flow.",
                "action_type": action,
                "action_payload": {"duration_minutes": RECOMMEND_DURATION_MIN},
            }, "default_nudge"

    return None, None


# ── ClickHouse query functions ────────────────────────────────────────────────

def get_active_user_ids(hours: int = 24) -> list[UUID]:
    """Return distinct user_ids with at least one event in the last `hours` hours."""
    client = get_clickhouse_client()
    if not client:
        return []
    try:
        r = client.query(f"""
        SELECT DISTINCT user_id
        FROM omni_analytics.telemetry_events
        WHERE at >= now() - toIntervalHour({hours})
        """)
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
    """Aggregate usage (focus vs distraction) and session stats for one user over the last `hours`."""
    client = get_clickhouse_client()
    if not client:
        return None
    uid = str(user_id)
    try:
        usage_r = client.query(f"""
        SELECT
            sumIf(duration_seconds, {FOCUS_SQL}) AS focus_seconds,
            sumIf(duration_seconds, {DISTRACTION_SQL}) AS distraction_seconds,
            argMax(category, duration_seconds) FILTER (WHERE {DISTRACTION_SQL}) AS top_distraction
        FROM omni_analytics.telemetry_events
        WHERE event_type = 'usage'
          AND user_id = '{uid}'
          AND at >= now() - toIntervalHour({hours})
        """)
        focus_seconds = 0
        distraction_seconds = 0
        top_distraction_category: str | None = None
        if usage_r.result_set:
            row = usage_r.result_set[0]
            focus_seconds = int(row[0] or 0)
            distraction_seconds = int(row[1] or 0)
            if row[2]:
                top_distraction_category = str(row[2]).strip() or None

        session_r = client.query(f"""
        SELECT count() AS cnt, sum(duration_seconds) AS total, max(duration_seconds) AS mx
        FROM omni_analytics.telemetry_events
        WHERE event_type = 'session'
          AND user_id = '{uid}'
          AND at >= now() - toIntervalHour({hours})
        """)
        session_count = 0
        total_session_seconds = 0
        max_session_seconds = 0
        if session_r.result_set:
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


def get_user_baseline(user_id: UUID, days: int = 30) -> UserBaseline | None:
    """
    Build a 30-day behavioral profile: avg daily focus, session length, distraction, peak hour.
    Returns None if the user has fewer than 3 active days (not enough history).
    """
    client = get_clickhouse_client()
    if not client:
        return None
    uid = str(user_id)
    try:
        # Daily focus and distraction per calendar day
        daily_r = client.query(f"""
        SELECT
            toDate(at) AS day,
            sumIf(duration_seconds, {FOCUS_SQL}) AS daily_focus,
            sumIf(duration_seconds, {DISTRACTION_SQL}) AS daily_distraction
        FROM omni_analytics.telemetry_events
        WHERE event_type = 'usage'
          AND user_id = '{uid}'
          AND at >= now() - toIntervalDay({days})
        GROUP BY day
        HAVING daily_focus + daily_distraction > 0
        """)
        rows = daily_r.result_set or []
        active_days = len(rows)
        if active_days < 3:
            return None  # not enough history for personalization

        avg_daily_focus = int(sum(r[1] for r in rows) / active_days)
        avg_daily_distraction = int(sum(r[2] for r in rows) / active_days)

        # Average session length (exclude sessions shorter than 5 min)
        session_r = client.query(f"""
        SELECT avg(duration_seconds) AS avg_len
        FROM omni_analytics.telemetry_events
        WHERE event_type = 'session'
          AND user_id = '{uid}'
          AND at >= now() - toIntervalDay({days})
          AND duration_seconds > 300
        """)
        avg_session = 0
        if session_r.result_set and session_r.result_set[0][0]:
            avg_session = int(session_r.result_set[0][0])

        # Peak productive hour (most focus time concentrated in which hour)
        peak_r = client.query(f"""
        SELECT toHour(at) AS hr, sum(duration_seconds) AS total
        FROM omni_analytics.telemetry_events
        WHERE event_type = 'usage'
          AND user_id = '{uid}'
          AND {FOCUS_SQL}
          AND at >= now() - toIntervalDay({days})
        GROUP BY hr
        ORDER BY total DESC
        LIMIT 1
        """)
        peak_hour: int | None = None
        if peak_r.result_set and peak_r.result_set[0]:
            peak_hour = int(peak_r.result_set[0][0])

        return UserBaseline(
            avg_daily_focus_seconds=avg_daily_focus,
            avg_session_length_seconds=avg_session,
            avg_daily_distraction_seconds=avg_daily_distraction,
            peak_hour=peak_hour,
            active_days=active_days,
        )
    except Exception as e:
        logger.warning("get_user_baseline failed for %s: %s", user_id, e)
        return None


def get_streak_days(user_id: UUID) -> int:
    """Count consecutive calendar days (most recent first) with ≥30 min of focus."""
    client = get_clickhouse_client()
    if not client:
        return 0
    uid = str(user_id)
    try:
        r = client.query(f"""
        SELECT toDate(at) AS day
        FROM omni_analytics.telemetry_events
        WHERE event_type = 'usage'
          AND user_id = '{uid}'
          AND {FOCUS_SQL}
          AND at >= now() - toIntervalDay(60)
        GROUP BY day
        HAVING sum(duration_seconds) >= {STREAK_MIN_DAILY_FOCUS_SECONDS}
        ORDER BY day DESC
        """)
        rows = r.result_set or []
        if not rows:
            return 0

        # Normalize all row values to date objects
        active_days: set[date] = set()
        for row in rows:
            d = row[0]
            if isinstance(d, datetime):
                d = d.date()
            elif not isinstance(d, date):
                try:
                    d = date.fromisoformat(str(d))
                except (ValueError, TypeError):
                    continue
            active_days.add(d)

        today = date.today()
        # Streak starts from today; if user hasn't worked today yet, start from yesterday
        start = today if today in active_days else today - timedelta(days=1)
        if start not in active_days:
            return 0

        streak = 0
        current = start
        while current in active_days:
            streak += 1
            current -= timedelta(days=1)
        return streak
    except Exception as e:
        logger.warning("get_streak_days failed for %s: %s", user_id, e)
        return 0


# ── High-level entry points ───────────────────────────────────────────────────

def build_user_context(user_id: UUID) -> RecommendationContext | None:
    """
    Gather all data needed for recommendation from ClickHouse + PostgreSQL.
    Returns None if no activity found in the last 4h (nothing to recommend on).
    """
    from app.notifications_db import get_last_notification

    agg_4h = get_user_aggregates(user_id, 4)
    if not agg_4h:
        return None

    agg_24h = get_user_aggregates(user_id, 24)
    agg_7d = get_user_aggregates(user_id, 24 * 7)
    baseline = get_user_baseline(user_id, days=30)
    streak = get_streak_days(user_id)
    last_action_type, last_seconds_ago = get_last_notification(user_id)

    return RecommendationContext(
        focus_4h_seconds=agg_4h.focus_seconds,
        distraction_4h_seconds=agg_4h.distraction_seconds,
        session_count_4h=agg_4h.session_count,
        top_distraction_category=agg_4h.top_distraction_category,
        focus_24h_seconds=agg_24h.focus_seconds if agg_24h else 0,
        distraction_24h_seconds=agg_24h.distraction_seconds if agg_24h else 0,
        focus_7d_seconds=agg_7d.focus_seconds if agg_7d else 0,
        baseline=baseline,
        streak_days=streak,
        hour_of_day=datetime.now().hour,
        last_action_type=last_action_type,
        last_notification_seconds_ago=last_seconds_ago,
    )


def run_analysis_for_user(user_id: UUID) -> dict[str, Any] | None:
    """Load context for user and return one recommendation dict, or None."""
    ctx = build_user_context(user_id)
    if not ctx:
        return None
    rec, _ = build_recommendation(ctx)
    return rec
