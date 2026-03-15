"""
APScheduler job: every 10 minutes, get active users from ClickHouse,
run recommendation algorithm per user, write one notification per user to PostgreSQL.
"""
from __future__ import annotations

import logging
from uuid import UUID

from apscheduler.schedulers.background import BackgroundScheduler

from app.algorithm import get_active_user_ids, run_analysis_for_user
from app.notifications_db import insert_notification

logger = logging.getLogger(__name__)

_scheduler: BackgroundScheduler | None = None


def run_recommendation_job_once() -> dict:
    """
    Run the recommendation job once (active users from CH -> algorithm -> PG).
    Returns {"active_users": N, "notifications_written": M, "sample": [...]} for testing.
    """
    user_ids = get_active_user_ids(hours=24)
    if not user_ids:
        return {"active_users": 0, "notifications_written": 0, "sample": []}
    written = 0
    sample: list[dict] = []
    for user_id in user_ids:
        try:
            rec = run_analysis_for_user(user_id)
            if not rec:
                continue
            ok = insert_notification(
                user_id=user_id,
                type_=rec.get("type", "recommendation"),
                title=rec.get("title"),
                body=rec.get("body"),
                action_type=rec.get("action_type"),
                action_payload=rec.get("action_payload"),
            )
            if ok:
                written += 1
                if len(sample) < 5:
                    sample.append({"user_id": str(user_id), "title": rec.get("title")})
                logger.debug("Inserted notification for user %s: %s", user_id, rec.get("title"))
        except Exception as e:
            logger.warning("Recommendation run failed for %s: %s", user_id, e)
    return {"active_users": len(user_ids), "notifications_written": written, "sample": sample}


def _run_recommendation_job() -> None:
    """Job body: active users -> run analysis -> insert one notification per user."""
    run_recommendation_job_once()


def start_scheduler() -> None:
    """Start the background scheduler; job runs every 10 minutes."""
    global _scheduler
    if _scheduler is not None:
        return
    _scheduler = BackgroundScheduler()
    _scheduler.add_job(_run_recommendation_job, "interval", minutes=10, id="recommendations")
    _scheduler.start()
    logger.info("Recommendation scheduler started (interval=10 min)")


def stop_scheduler() -> None:
    """Shut down the scheduler."""
    global _scheduler
    if _scheduler is None:
        return
    _scheduler.shutdown(wait=False)
    _scheduler = None
    logger.info("Recommendation scheduler stopped")
