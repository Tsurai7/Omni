FROM python:3.12-slim

WORKDIR /app

# Install dependencies (psycopg2-binary required for SQLAlchemy postgres dialect)
COPY pyproject.toml ./
RUN pip install --no-cache-dir fastapi "uvicorn[standard]" clickhouse-connect sqlalchemy pandas "psycopg2-binary>=2.9.0" scikit-learn

COPY app/ ./app/

EXPOSE 8000
CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8000"]
