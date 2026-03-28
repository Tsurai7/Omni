"""
APScheduler jobs:
  - Every 10 minutes: recommendation analysis for active users
  - Every Monday at 09:00: weekly digest for all users with recent activity
  - Every 10 minutes: streak milestone check
"""
from __future__ import annotations

import logging
from uuid import UUID

from apscheduler.schedulers.background import BackgroundScheduler

from app.algorithm import (
    check_streak_milestone,
    generate_weekly_digest,
    get_active_user_ids,
    get_streak_days,
    run_analysis_for_user,
)
from app.embedding_indexer import run_embedding_indexer
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


def run_streak_milestone_check() -> None:
    """Check all active users for streak milestones and send celebration notifications."""
    user_ids = get_active_user_ids(hours=48)
    for user_id in user_ids:
        try:
            streak = get_streak_days(user_id)
            milestone = check_streak_milestone(streak)
            if milestone:
                insert_notification(
                    user_id=user_id,
                    type_=milestone.get("type", "milestone"),
                    title=milestone.get("title"),
                    body=milestone.get("body"),
                    action_type=milestone.get("action_type"),
                    action_payload=milestone.get("action_payload"),
                )
                logger.info("Streak milestone for user %s: %s days", user_id, streak)
        except Exception as e:
            logger.warning("Streak milestone check failed for %s: %s", user_id, e)


def run_weekly_digest_job() -> dict:
    """
    Generate and send weekly digest to all users with recent activity.
    Intended to run every Monday morning.
    """
    user_ids = get_active_user_ids(hours=24 * 7)
    if not user_ids:
        return {"users_checked": 0, "digests_sent": 0}
    sent = 0
    for user_id in user_ids:
        try:
            digest = generate_weekly_digest(user_id)
            if not digest:
                continue
            ok = insert_notification(
                user_id=user_id,
                type_=digest.get("type", "weekly_digest"),
                title=digest.get("title"),
                body=digest.get("body"),
                action_type=digest.get("action_type"),
                action_payload=digest.get("action_payload"),
            )
            if ok:
                sent += 1
                logger.info("Sent weekly digest to user %s", user_id)
        except Exception as e:
            logger.warning("Weekly digest failed for %s: %s", user_id, e)
    return {"users_checked": len(user_ids), "digests_sent": sent}


def _run_recommendation_job() -> None:
    run_recommendation_job_once()


def start_scheduler() -> None:
    """Start the background scheduler with all recurring jobs."""
    global _scheduler
    if _scheduler is not None:
        return
    _scheduler = BackgroundScheduler()

    # Main recommendation engine — every 10 minutes
    _scheduler.add_job(_run_recommendation_job, "interval", minutes=10, id="recommendations")

    # Streak milestone check — every 10 minutes (lightweight, checks milestones only)
    _scheduler.add_job(run_streak_milestone_check, "interval", minutes=10, id="streak_milestones")

    # Weekly digest — every Monday at 09:00
    _scheduler.add_job(run_weekly_digest_job, "cron", day_of_week="mon", hour=9, minute=0, id="weekly_digest")

    # RAG embedding indexer — every 5 minutes (best-effort, non-fatal)
    _scheduler.add_job(run_embedding_indexer, "interval", minutes=5, id="embedding_indexer")

    _scheduler.start()
    logger.info("Scheduler started: recommendations(10m), streak_milestones(10m), weekly_digest(Mon 09:00), embedding_indexer(5m)")


def stop_scheduler() -> None:
    """Shut down the scheduler."""
    global _scheduler
    if _scheduler is None:
        return
    _scheduler.shutdown(wait=False)
    _scheduler = None
    logger.info("Scheduler stopped")
