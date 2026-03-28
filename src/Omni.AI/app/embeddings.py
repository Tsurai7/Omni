"""
RAG: embedding generation (Gemini text-embedding-004) + pgvector storage + semantic search.
"""
from __future__ import annotations

import json
import logging
from uuid import UUID

import google.generativeai as genai

from app.db import get_pg_engine

logger = logging.getLogger(__name__)

EMBEDDING_MODEL = "models/text-embedding-004"
EMBEDDING_DIMS = 768  # text-embedding-004 fixed output size


# ── Vector helpers ─────────────────────────────────────────────────────────────

def _fmt_vector(embedding: list[float]) -> str:
    """Format a float list as a PostgreSQL vector literal '[x,y,...]'."""
    return "[" + ",".join(f"{x:.8f}" for x in embedding) + "]"


# ── Embedding generation ───────────────────────────────────────────────────────

def generate_embedding(content: str, task_type: str = "retrieval_document") -> list[float] | None:
    """
    Generate a 768-dim embedding via Gemini text-embedding-004.
    task_type: 'retrieval_document' for indexing, 'retrieval_query' for search.
    Returns None on failure (non-fatal — indexing is best-effort).
    """
    try:
        result = genai.embed_content(
            model=EMBEDDING_MODEL,
            content=content,
            task_type=task_type,
        )
        return result["embedding"]
    except Exception as e:
        logger.warning("Embedding generation failed: %s", e)
        return None


# ── Storage ────────────────────────────────────────────────────────────────────

def upsert_embedding(
    user_id: UUID | None,
    source_type: str,
    source_id: str,
    content_text: str,
    metadata: dict | None = None,
) -> bool:
    """
    Generate and store/update an embedding in pgvector.
    ON CONFLICT (source_type, source_id) → updates content + vector.
    Returns True on success, False on any failure.
    """
    embedding = generate_embedding(content_text)
    if not embedding:
        return False

    engine = get_pg_engine()
    if not engine:
        return False

    try:
        from sqlalchemy import text
        with engine.connect() as conn:
            conn.execute(
                text("""
                    INSERT INTO embeddings
                        (user_id, source_type, source_id, content_text, embedding, metadata, updated_at)
                    VALUES
                        (:user_id, :source_type, :source_id, :content_text,
                         :embedding::vector, :metadata, NOW())
                    ON CONFLICT (source_type, source_id) DO UPDATE SET
                        content_text = EXCLUDED.content_text,
                        embedding    = EXCLUDED.embedding,
                        metadata     = EXCLUDED.metadata,
                        updated_at   = NOW()
                """),
                {
                    "user_id": str(user_id) if user_id else None,
                    "source_type": source_type,
                    "source_id": source_id,
                    "content_text": content_text,
                    "embedding": _fmt_vector(embedding),
                    "metadata": json.dumps(metadata) if metadata else None,
                },
            )
            conn.commit()
        return True
    except Exception as e:
        logger.warning("upsert_embedding failed (source_type=%s, source_id=%s): %s", source_type, source_id, e)
        return False


# ── Semantic search ────────────────────────────────────────────────────────────

def search_similar(
    user_id: UUID,
    query: str,
    source_types: list[str] | None = None,
    limit: int = 5,
    score_threshold: float = 0.30,
) -> list[dict]:
    """
    Semantic search across user's embeddings (sessions, tasks, chat) + global knowledge base.

    Returns list of dicts with keys:
      source_type, source_id, content_text, metadata, similarity (0-1).
    """
    query_emb = generate_embedding(query, task_type="retrieval_query")
    if not query_emb:
        return []

    engine = get_pg_engine()
    if not engine:
        return []

    type_filter = ""
    if source_types:
        placeholders = ",".join(f"'{t}'" for t in source_types if t.isalpha() or "_" in t)
        if placeholders:
            type_filter = f"AND source_type IN ({placeholders})"

    try:
        from sqlalchemy import text
        with engine.connect() as conn:
            rows = conn.execute(
                text(f"""
                    SELECT
                        source_type,
                        source_id,
                        content_text,
                        metadata,
                        1 - (embedding <=> :query_emb::vector) AS similarity
                    FROM embeddings
                    WHERE (user_id = :user_id OR user_id IS NULL)
                    {type_filter}
                    AND 1 - (embedding <=> :query_emb::vector) > :threshold
                    ORDER BY embedding <=> :query_emb::vector
                    LIMIT :limit
                """),
                {
                    "user_id": str(user_id),
                    "query_emb": _fmt_vector(query_emb),
                    "threshold": score_threshold,
                    "limit": limit,
                },
            ).fetchall()

        return [
            {
                "source_type": r[0],
                "source_id": str(r[1]),
                "content_text": r[2],
                "metadata": r[3],
                "similarity": round(float(r[4]), 3),
            }
            for r in rows
        ]
    except Exception as e:
        logger.warning("search_similar failed: %s", e)
        return []
