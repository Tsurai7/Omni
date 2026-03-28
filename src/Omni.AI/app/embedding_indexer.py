"""
Background embedding indexer.
Picks up sessions, tasks, and chat messages that don't have embeddings yet
and indexes them in batches. Runs every 5 minutes via APScheduler.
"""
from __future__ import annotations

import logging
from datetime import datetime

from app.db import get_pg_engine
from app.embeddings import upsert_embedding

logger = logging.getLogger(__name__)

_BATCH = 50


def _build_session_text(row: tuple) -> str:
    """Compose a rich text description of a session for embedding."""
    session_id, user_id, name, activity_type, started_at, duration_seconds, intention, subjective_rating, reflection_note = row
    parts = [f"Focus session: {name}"]
    if activity_type and activity_type != "other":
        parts[0] += f" ({activity_type})"
    if duration_seconds:
        mins = int(duration_seconds) // 60
        parts[0] += f", {mins} minutes"
    if started_at:
        try:
            date_str = started_at.strftime("%B %d, %Y") if isinstance(started_at, datetime) else str(started_at)[:10]
            parts.append(f"Date: {date_str}")
        except Exception:
            pass
    if intention:
        parts.append(f"Intention: {intention}")
    if reflection_note:
        parts.append(f"Reflection: {reflection_note}")
    if subjective_rating:
        parts.append(f"Rating: {subjective_rating}/10")
    return ". ".join(parts)


def _build_task_text(row: tuple) -> str:
    """Compose a text description of a task for embedding."""
    task_id, user_id, title, status, priority, created_at, due_date = row
    parts = [f"Task: {title}"]
    parts.append(f"Status: {status}, Priority: {priority}")
    if created_at:
        try:
            date_str = created_at.strftime("%B %d, %Y") if isinstance(created_at, datetime) else str(created_at)[:10]
            parts.append(f"Created: {date_str}")
        except Exception:
            pass
    if due_date:
        try:
            due_str = due_date.strftime("%B %d, %Y") if isinstance(due_date, datetime) else str(due_date)[:10]
            parts.append(f"Due: {due_str}")
        except Exception:
            pass
    return ". ".join(parts)


def index_pending_sessions(batch_size: int = _BATCH) -> int:
    """
    Index sessions that have reflection_note but no embedding yet.
    Returns count of successfully indexed sessions.
    """
    engine = get_pg_engine()
    if not engine:
        return 0

    try:
        from sqlalchemy import text
        with engine.connect() as conn:
            rows = conn.execute(
                text("""
                    SELECT
                        s.id, s.user_id, s.name, s.activity_type,
                        s.started_at, s.duration_seconds,
                        s.intention, s.subjective_rating, s.reflection_note
                    FROM sessions s
                    LEFT JOIN embeddings e
                        ON e.source_id = s.id::text
                        AND e.source_type = 'session'
                    WHERE s.reflection_note IS NOT NULL
                      AND s.reflection_note != ''
                      AND e.id IS NULL
                    ORDER BY s.started_at DESC
                    LIMIT :batch
                """),
                {"batch": batch_size},
            ).fetchall()
    except Exception as e:
        logger.warning("index_pending_sessions fetch failed: %s", e)
        return 0

    indexed = 0
    for row in rows:
        session_id, user_id = row[0], row[1]
        content = _build_session_text(row)
        meta = {
            "name": row[2],
            "activity_type": row[3],
            "started_at": str(row[4])[:10] if row[4] else None,
            "duration_minutes": int(row[5]) // 60 if row[5] else None,
            "rating": row[7],
        }
        ok = upsert_embedding(user_id, "session", str(session_id), content, meta)
        if ok:
            indexed += 1

    if indexed:
        logger.info("Indexed %d sessions", indexed)
    return indexed


def index_pending_tasks(batch_size: int = _BATCH) -> int:
    """
    Index tasks that don't have an embedding yet (or were updated recently).
    Returns count of successfully indexed tasks.
    """
    engine = get_pg_engine()
    if not engine:
        return 0

    try:
        from sqlalchemy import text
        with engine.connect() as conn:
            rows = conn.execute(
                text("""
                    SELECT
                        t.id, t.user_id, t.title, t.status, t.priority,
                        t.created_at, t.due_date
                    FROM tasks t
                    LEFT JOIN embeddings e
                        ON e.source_id = t.id::text
                        AND e.source_type = 'task'
                    WHERE t.status != 'cancelled'
                      AND (
                          e.id IS NULL
                          OR e.updated_at < t.updated_at
                      )
                    ORDER BY t.updated_at DESC
                    LIMIT :batch
                """),
                {"batch": batch_size},
            ).fetchall()
    except Exception as e:
        logger.warning("index_pending_tasks fetch failed: %s", e)
        return 0

    indexed = 0
    for row in rows:
        task_id, user_id = row[0], row[1]
        content = _build_task_text(row)
        meta = {
            "title": row[2],
            "status": row[3],
            "priority": row[4],
        }
        ok = upsert_embedding(user_id, "task", str(task_id), content, meta)
        if ok:
            indexed += 1

    if indexed:
        logger.info("Indexed %d tasks", indexed)
    return indexed


def run_embedding_indexer() -> None:
    """Main entry point called by APScheduler every 5 minutes."""
    try:
        sessions = index_pending_sessions()
        tasks = index_pending_tasks()
        if sessions or tasks:
            logger.info("Embedding indexer: sessions=%d, tasks=%d", sessions, tasks)
    except Exception as e:
        logger.error("Embedding indexer failed: %s", e)
