"""
Simulation layer for the recommendation engine.

Allows testing any scenario instantly — no ClickHouse or PostgreSQL required.
All inputs are expressed in minutes (more readable than seconds in JSON).
"""
from __future__ import annotations

from typing import Any, Literal

from pydantic import BaseModel, Field

from app.algorithm import RecommendationContext, UserBaseline, build_recommendation


class SimulationInput(BaseModel):
    """All inputs to the recommendation engine, expressed in minutes for readability."""
    # 4h window
    focus_4h_minutes: int = 0
    distraction_4h_minutes: int = 0
    session_count_4h: int = 0
    top_distraction_category: str | None = None
    # 24h window
    focus_24h_minutes: int = 0
    distraction_24h_minutes: int = 0
    # 7d window
    focus_7d_minutes: int = 0
    # personalized baseline (omit any field to use fixed thresholds)
    avg_daily_focus_minutes: int | None = None
    avg_session_length_minutes: int | None = None
    avg_daily_distraction_minutes: int | None = None
    peak_hour: int | None = Field(default=None, ge=0, le=23)
    # context
    streak_days: int = 0
    hour_of_day: int = Field(default=12, ge=0, le=23)
    # cooldown simulation: set these to test anti-spam behavior
    last_recommendation_type: str | None = None
    last_recommendation_minutes_ago: int | None = None


class SimulationRequest(BaseModel):
    scenario: Literal[
        "burnout",
        "distracted",
        "no_activity",
        "productive_streak",
        "low_week",
        "peak_hour",
        "custom",
    ] = "burnout"
    custom: SimulationInput | None = None


# ── Predefined scenarios ──────────────────────────────────────────────────────
# Each scenario is designed to trigger a specific rule so you can verify behavior.

SCENARIOS: dict[str, SimulationInput] = {
    # Rule 1: sustained_focus_burnout
    # focus_4h (135min) > avg_session_length (55min) × 1.5 = 82.5min, no sessions
    "burnout": SimulationInput(
        focus_4h_minutes=135,
        distraction_4h_minutes=5,
        session_count_4h=0,
        focus_24h_minutes=280,
        focus_7d_minutes=1400,
        avg_daily_focus_minutes=180,
        avg_session_length_minutes=55,
        avg_daily_distraction_minutes=20,
        streak_days=1,
        hour_of_day=14,
    ),
    # Rule 4: high_distraction
    # distraction_4h (55min) >> threshold (max 20min, 25/4×0.5 ≈ 3min) = 20min
    "distracted": SimulationInput(
        focus_4h_minutes=10,
        distraction_4h_minutes=55,
        top_distraction_category="Gaming",
        session_count_4h=0,
        focus_24h_minutes=30,
        distraction_24h_minutes=110,
        focus_7d_minutes=900,
        avg_daily_focus_minutes=150,
        avg_daily_distraction_minutes=25,
        streak_days=0,
        hour_of_day=15,
    ),
    # Rule 6 fallback: focus_down_trend_7d
    # No 4h activity, no baseline → falls through to trend comparison
    "no_activity": SimulationInput(
        focus_4h_minutes=0,
        distraction_4h_minutes=0,
        session_count_4h=0,
        focus_24h_minutes=0,
        focus_7d_minutes=600,
        avg_daily_focus_minutes=150,
        streak_days=0,
        hour_of_day=10,
    ),
    # Rule 2: streak_celebration
    # streak_days=5 ≥ 3 and no recent notification
    "productive_streak": SimulationInput(
        focus_4h_minutes=45,
        distraction_4h_minutes=5,
        session_count_4h=1,
        focus_24h_minutes=210,
        focus_7d_minutes=1500,
        avg_daily_focus_minutes=180,
        avg_session_length_minutes=50,
        avg_daily_distraction_minutes=20,
        streak_days=5,
        hour_of_day=16,
    ),
    # Rule 6: focus_down_trend
    # focus_24h (45min) < avg_daily (200min) × 0.7 = 140min
    "low_week": SimulationInput(
        focus_4h_minutes=20,
        distraction_4h_minutes=10,
        session_count_4h=0,
        focus_24h_minutes=45,
        focus_7d_minutes=1400,
        avg_daily_focus_minutes=200,
        avg_daily_distraction_minutes=30,
        streak_days=0,
        hour_of_day=14,
    ),
    # Rule 3: peak_hour_nudge
    # hour_of_day matches peak_hour and focus_4h < 20min
    "peak_hour": SimulationInput(
        focus_4h_minutes=15,
        distraction_4h_minutes=5,
        session_count_4h=0,
        focus_24h_minutes=50,
        focus_7d_minutes=1100,
        avg_daily_focus_minutes=160,
        avg_session_length_minutes=45,
        avg_daily_distraction_minutes=20,
        streak_days=2,
        hour_of_day=10,
        peak_hour=10,
    ),
}


def _build_context(inputs: SimulationInput) -> RecommendationContext:
    """Convert SimulationInput (minutes) → RecommendationContext (seconds)."""
    has_baseline = any([
        inputs.avg_daily_focus_minutes is not None,
        inputs.avg_session_length_minutes is not None,
        inputs.avg_daily_distraction_minutes is not None,
        inputs.peak_hour is not None,
    ])
    baseline: UserBaseline | None = None
    if has_baseline:
        baseline = UserBaseline(
            avg_daily_focus_seconds=(inputs.avg_daily_focus_minutes or 0) * 60,
            avg_session_length_seconds=(inputs.avg_session_length_minutes or 0) * 60,
            avg_daily_distraction_seconds=(inputs.avg_daily_distraction_minutes or 0) * 60,
            peak_hour=inputs.peak_hour,
            active_days=14,  # assumed for simulation
        )

    return RecommendationContext(
        focus_4h_seconds=inputs.focus_4h_minutes * 60,
        distraction_4h_seconds=inputs.distraction_4h_minutes * 60,
        session_count_4h=inputs.session_count_4h,
        top_distraction_category=inputs.top_distraction_category,
        focus_24h_seconds=inputs.focus_24h_minutes * 60,
        distraction_24h_seconds=inputs.distraction_24h_minutes * 60,
        focus_7d_seconds=inputs.focus_7d_minutes * 60,
        baseline=baseline,
        streak_days=inputs.streak_days,
        hour_of_day=inputs.hour_of_day,
        last_action_type=inputs.last_recommendation_type,
        last_notification_seconds_ago=(
            inputs.last_recommendation_minutes_ago * 60
            if inputs.last_recommendation_minutes_ago is not None
            else None
        ),
    )


def run_simulation(req: SimulationRequest) -> dict[str, Any]:
    """
    Run the recommendation engine with simulated inputs. Pure — no DB calls.
    Returns the inputs used, the recommendation produced, and which rule fired.
    """
    if req.scenario == "custom":
        inputs = req.custom or SimulationInput()
    else:
        inputs = SCENARIOS.get(req.scenario, SCENARIOS["burnout"])

    ctx = _build_context(inputs)
    rec, rule = build_recommendation(ctx)

    return {
        "scenario": req.scenario,
        "inputs": inputs.model_dump(),
        "recommendation": rec,
        "rule_triggered": rule,
    }
