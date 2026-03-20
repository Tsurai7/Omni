"""
Chat persistence — CRUD for chat_conversations and chat_messages tables.
"""
from __future__ import annotations

import json
import logging
from uuid import UUID

from sqlalchemy import text

from app.db import get_pg_engine

logger = logging.getLogger(__name__)


def create_conversation(user_id: UUID, title: str) -> str | None:
    """Insert a new conversation row and return its UUID string."""
    engine = get_pg_engine()
    if not engine:
        return None
    try:
        with engine.connect() as conn:
            row = conn.execute(
                text("""
                INSERT INTO chat_conversations (user_id, title)
                VALUES (:user_id, :title)
                RETURNING id
                """),
                {"user_id": str(user_id), "title": title},
            ).fetchone()
            conn.commit()
            return str(row[0]) if row else None
    except Exception as e:
        logger.warning("create_conversation failed for %s: %s", user_id, e)
        return None


def get_conversations(user_id: UUID, limit: int = 10) -> list[dict]:
    """Return the user's most recent conversations (non-deleted)."""
    engine = get_pg_engine()
    if not engine:
        return []
    try:
        with engine.connect() as conn:
            rows = conn.execute(
                text("""
                SELECT id, title, created_at, last_message_at
                FROM chat_conversations
                WHERE user_id = :user_id AND deleted_at IS NULL
                ORDER BY last_message_at DESC
                LIMIT :limit
                """),
                {"user_id": str(user_id), "limit": limit},
            ).fetchall()
        return [
            {
                "id": str(r[0]),
                "title": r[1],
                "created_at": r[2].isoformat() if r[2] else None,
                "last_message_at": r[3].isoformat() if r[3] else None,
            }
            for r in rows
        ]
    except Exception as e:
        logger.warning("get_conversations failed for %s: %s", user_id, e)
        return []


def get_messages(conversation_id: str, limit: int = 20) -> list[dict]:
    """Return messages for a conversation ordered oldest-first (for LLM context)."""
    engine = get_pg_engine()
    if not engine:
        return []
    try:
        with engine.connect() as conn:
            rows = conn.execute(
                text("""
                SELECT id, role, content, metadata, created_at
                FROM chat_messages
                WHERE conversation_id = :conv_id
                ORDER BY created_at ASC
                LIMIT :limit
                """),
                {"conv_id": conversation_id, "limit": limit},
            ).fetchall()
        return [
            {
                "id": str(r[0]),
                "role": r[1],
                "content": r[2],
                "metadata": r[3],
                "created_at": r[4].isoformat() if r[4] else None,
            }
            for r in rows
        ]
    except Exception as e:
        logger.warning("get_messages failed for conv %s: %s", conversation_id, e)
        return []


def insert_message(
    conversation_id: str,
    user_id: UUID,
    role: str,
    content: str,
    metadata: dict | None = None,
) -> bool:
    """Insert a message and bump conversation's last_message_at."""
    engine = get_pg_engine()
    if not engine:
        return False
    payload_json = json.dumps(metadata) if metadata is not None else None
    try:
        with engine.connect() as conn:
            conn.execute(
                text("""
                INSERT INTO chat_messages (conversation_id, user_id, role, content, metadata)
                VALUES (:conv_id, :user_id, :role, :content, CAST(:metadata AS jsonb))
                """),
                {
                    "conv_id": conversation_id,
                    "user_id": str(user_id),
                    "role": role,
                    "content": content,
                    "metadata": payload_json,
                },
            )
            conn.execute(
                text("""
                UPDATE chat_conversations
                SET last_message_at = NOW()
                WHERE id = :conv_id
                """),
                {"conv_id": conversation_id},
            )
            conn.commit()
        return True
    except Exception as e:
        logger.warning("insert_message failed: %s", e)
        return False


def delete_conversation(user_id: UUID, conversation_id: str) -> bool:
    """Soft-delete a conversation (only if it belongs to the user)."""
    engine = get_pg_engine()
    if not engine:
        return False
    try:
        with engine.connect() as conn:
            conn.execute(
                text("""
                UPDATE chat_conversations
                SET deleted_at = NOW()
                WHERE id = :conv_id AND user_id = :user_id AND deleted_at IS NULL
                """),
                {"conv_id": conversation_id, "user_id": str(user_id)},
            )
            conn.commit()
        return True
    except Exception as e:
        logger.warning("delete_conversation failed: %s", e)
        return False


def conversation_belongs_to_user(conversation_id: str, user_id: UUID) -> bool:
    """Check that a conversation exists and belongs to the given user."""
    engine = get_pg_engine()
    if not engine:
        return False
    try:
        with engine.connect() as conn:
            row = conn.execute(
                text("""
                SELECT 1 FROM chat_conversations
                WHERE id = :conv_id AND user_id = :user_id AND deleted_at IS NULL
                """),
                {"conv_id": conversation_id, "user_id": str(user_id)},
            ).fetchone()
        return row is not None
    except Exception as e:
        logger.warning("conversation_belongs_to_user failed: %s", e)
        return False
