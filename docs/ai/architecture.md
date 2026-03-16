# System Architecture Specification

## 1. End-State Vision
The final product is a high-performance, cross-platform productivity tracking system. It allows users to manage tasks and automatically tracks their work focus (telemetry) to prevent cognitive burnout.

The system is designed for **high availability and high throughput**:
- **Offline First:** The desktop client (.NET MAUI) uses a local SQLite database to cache tasks and telemetry when offline, syncing automatically when the connection is restored. The client now implements this: tasks and pending usage/session payloads are stored in SQLite and drained by a background Sync Service when the API is reachable.
- **Microservice Backend:** A highly concurrent Go backend routes requests, manages transactional data (PostgreSQL), and ingests high-frequency telemetry.
- **Big Data & AI Pipeline:** Telemetry is buffered through Red panda and stored in ClickHouse (columnar DB) for ultra-fast aggregations. A Python AI microservice periodically analyzes this data to detect burnout patterns and writes personalized recommendations back to the transactional database.

## 2. Architecture Diagram

```mermaid
graph TD
    subgraph Client Tier .NET MAUI
        UI[UI macOS / Windows]
        SQLite[(Local DB SQLite<br>DONE)]
        UI <-->|Offline Caching| SQLite
    end

    subgraph Business Logic Tier Go
        API[REST API Gateway]
        Prof[Profile Service<br>DONE]
        Task[Task Service<br>DONE]
        Tele[Telemetry Worker<br>DONE]

        UI <-->|HTTP / JSON| API
        API --> Prof
        API --> Task
        API --> Tele
    end

    subgraph Transactional Data
        PG[(PostgreSQL)]
        Prof <-->|Read/Write| PG
        Task <-->|Read/Write| PG
    end

    subgraph Analytical Pipeline DONE
        RedPanda[RedPanda]
        CH[(ClickHouse)]
        AI[(AI Microservice)]

        Tele -->|Publish Events| RedPanda
        RedPanda -->|Consumer| CH
        CH -->|Read| AI
        AI -->|Write Recommendations| PG
    end

    classDef done fill:#d4edda,stroke:#28a745,stroke-width:2px;
    classDef pending fill:#fff3cd,stroke:#ffc107,stroke-width:2px,stroke-dasharray: 5 5;
    
    class Prof,Tele,API,UI,PG,RedPanda,CH,AI,SQLite,Task done;