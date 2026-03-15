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
