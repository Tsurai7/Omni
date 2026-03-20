"""
Write generated recommendations to PostgreSQL user_notifications table.
"""
from __future__ import annotations

import json
import logging
from uuid import UUID

from sqlalchemy import text

from app.db import get_pg_engine

logger = logging.getLogger(__name__)


def get_last_notification(user_id: UUID) -> tuple[str | None, int | None]:
    """
    Return (action_type, seconds_ago) of the most recent notification for this user.
    Used for cooldown checks — we don't filter by read_at so reading a notification
    doesn't immediately re-trigger the same recommendation.
    Returns (None, None) if no notifications exist or DB is unavailable.
    """
    engine = get_pg_engine()
    if not engine:
        return None, None
    try:
        with engine.connect() as conn:
            result = conn.execute(
                text("""
                SELECT action_type,
                       EXTRACT(EPOCH FROM (NOW() - created_at))::int AS seconds_ago
                FROM user_notifications
                WHERE user_id = :user_id
                ORDER BY created_at DESC
                LIMIT 1
                """),
                {"user_id": str(user_id)},
            )
            row = result.fetchone()
            if row and row[0]:
                return str(row[0]), int(row[1]) if row[1] is not None else None
            return None, None
    except Exception as e:
        logger.warning("get_last_notification failed for %s: %s", user_id, e)
        return None, None


def insert_notification(
    user_id: UUID,
    type_: str,
    title: str | None = None,
    body: str | None = None,
    action_type: str | None = None,
    action_payload: dict | None = None,
) -> bool:
    """
    Insert one row into user_notifications. Returns True on success.
    """
    engine = get_pg_engine()
    if not engine:
        logger.warning("PostgreSQL not configured, skip insert notification")
        return False
    payload_json = json.dumps(action_payload) if action_payload is not None else None
    try:
        with engine.connect() as conn:
            conn.execute(
                text("""
                INSERT INTO user_notifications (user_id, type, title, body, action_type, action_payload)
                VALUES (:user_id, :type, :title, :body, :action_type, CAST(:action_payload AS jsonb))
                """),
                {
                    "user_id": str(user_id),
                    "type": type_,
                    "title": title or "",
                    "body": body or "",
                    "action_type": action_type or "none",
                    "action_payload": payload_json,
                },
            )
            conn.commit()
        return True
    except Exception as e:
        logger.warning("insert_notification failed: %s", e)
        return False
