"""
Text chunking for knowledge base ingestion.
Splits long texts into overlapping chunks that fit within embedding context windows.
"""
from __future__ import annotations

import re
import uuid


MAX_CHARS = 1200   # ~300 tokens, well within Gemini 2048-token embedding limit
OVERLAP_CHARS = 150


def chunk_text(text: str, max_chars: int = MAX_CHARS, overlap: int = OVERLAP_CHARS) -> list[str]:
    """
    Split text into overlapping chunks, preferring paragraph/sentence boundaries.

    Strategy:
    1. Split by double-newline (paragraphs)
    2. If a paragraph is longer than max_chars, split by sentences
    3. Assemble chunks greedily, carry overlap from previous chunk
    """
    if len(text) <= max_chars:
        return [text.strip()] if text.strip() else []

    # Split into paragraphs first
    paragraphs = [p.strip() for p in re.split(r"\n\s*\n", text) if p.strip()]

    # If paragraphs are still too long, split by sentences
    segments: list[str] = []
    for para in paragraphs:
        if len(para) <= max_chars:
            segments.append(para)
        else:
            sentences = re.split(r"(?<=[.!?])\s+", para)
            segments.extend(s.strip() for s in sentences if s.strip())

    # Assemble segments into chunks with overlap
    chunks: list[str] = []
    current = ""
    carry = ""  # text to carry over for overlap

    for seg in segments:
        candidate = (carry + " " + seg).strip() if carry else seg
        if len(current) + len(candidate) + 1 <= max_chars:
            current = (current + " " + candidate).strip() if current else candidate
        else:
            if current:
                chunks.append(current)
                # Carry the last `overlap` chars as context for next chunk
                carry = current[-overlap:] if len(current) > overlap else current
            current = candidate

    if current:
        chunks.append(current)

    return chunks


def make_chunk_id(article_source: str, chunk_index: int) -> str:
    """Deterministic UUID for a knowledge chunk — stable across re-ingestion."""
    namespace = uuid.UUID("6ba7b810-9dad-11d1-80b4-00c04fd430c8")  # URL namespace
    name = f"{article_source}::{chunk_index}"
    return str(uuid.uuid5(namespace, name))
