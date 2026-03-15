# syntax=docker/dockerfile:1
# Use BuildKit for cache mounts: DOCKER_BUILDKIT=1 (default in Docker 23+)
FROM python:3.14-slim-bookworm

WORKDIR /app

# Install uv (fast pip replacement)
COPY --from=ghcr.io/astral-sh/uv:latest /uv /usr/local/bin/uv

# Install deps from pyproject.toml; cache mount reuses downloads across builds
ENV UV_CACHE_DIR=/cache/uv
COPY pyproject.toml ./
RUN --mount=type=cache,id=uv-cache,target=/cache/uv \
    uv pip compile pyproject.toml -o requirements.txt && \
    uv pip install --system -r requirements.txt

COPY app/ ./app/

EXPOSE 8000
CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8000"]
