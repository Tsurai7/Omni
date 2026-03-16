***

### File 2: `implementation_plan.md`

```markdown
# Implementation Plan & Checklist

This document tracks the progression of the architecture implementation. Use this to guide prompt context in Cursor.

## Phase 1: Foundation & Auth (✅ Completed)
- [x] Set up cross-platform .NET MAUI UI project.
- [x] Set up Go backend (API Gateway).
- [x] Provision PostgreSQL database.
- [x] Implement Profile Service (Auth, Registration, JWT).
- [x] Implement basic Telemetry ingestion endpoint on the Go backend.

## Phase 2: Core Business Logic (🚧 In Progress)
**Goal:** Finish the core task management and ensure the app works without an internet connection.

### 2.1 Task Service (Go)
- [x] Define `Task` models/structs in Go.
- [x] Implement database migrations for tasks in PostgreSQL.
- [x] Implement CRUD endpoints (`CreateTask`, `GetTasks`, `UpdateStatus`, `DeleteTask`).
- [x] Connect .NET MAUI UI to the new Task endpoints.

### 2.2 Local Caching (C# / .NET MAUI)
- [x] Integrate SQLite into the MAUI project.
- [x] Create local schema for `Task` and `TelemetryEvent`.
- [x] Implement local write logic (if offline, write to SQLite).
- [x] Implement background Sync Service (poll SQLite for `IsSynced == false`, send to Go API, mark as synced upon 200 OK).

## Phase 3: Analytical Infrastructure (⏳ Pending)
**Goal:** Deploy the big data pipeline for handling high-frequency telemetry.

### 3.1 Red panda Integration
- [x] Deploy Redpanda (via Docker Compose).
- [x] Update Go `Telemetry Worker` to act as a Red panda Producer (publish incoming JSON events to a Red panda topic).
- [x] Write integration tests to ensure Go successfully pushes to Red panda.

### 3.2 ClickHouse Integration
- [x] Deploy ClickHouse (via Docker Compose).
- [x] Create database schema in ClickHouse (e.g., `MergeTree` table for `telemetry_events`).
- [x] Implement a Red panda Consumer (either a Go worker or native ClickHouse Red panda Engine) to drain the Red panda topic into ClickHouse.

## Phase 4: AI & Recommendations (⏳ Pending)
**Goal:** Bring the system to its final state by adding intelligent burnout detection.

### 4.1 AI Microservice Setup (Python)
- [x] Initialize Python environment (FastAPI, Pandas, scikit-learn).
- [x] Implement database connections (`clickhouse-connect` for reading, `SQLAlchemy` for writing to PostgreSQL).

### 4.2 Algorithm & Delivery
- [ ] Write the anomaly detection algorithm (query CH for past 4 hours of focus, apply heuristic or Isolation Forest).
- [ ] Implement the scheduler (e.g., APScheduler) to run the analysis every 10 minutes per active user.
- [ ] Create the `user_notifications` table in PostgreSQL.
- [ ] Implement logic to write generated AI advice to PostgreSQL.
- [ ] Add an endpoint in the Go API to fetch notifications, and display them in the .NET MAUI UI.