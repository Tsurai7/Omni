# Auth Backend Design

**Date:** 2025-03-08  
**Scope:** Authorization and account creation backend for Omni (Go + Gin + PostgreSQL).

## Goal

Provide a backend in `src/Omni.Backend` that supports user registration and login, returning JWTs for use by the MAUI client (and future API consumers). User data is stored in PostgreSQL.

## Decisions

- **Persistence:** PostgreSQL (user chose option b).
- **Auth mechanism:** JWT bearer tokens (stateless, mobile-friendly).
- **Register response:** Include JWT so the user is logged in immediately (no separate login required after signup).

## Stack

- **Router:** Gin.
- **DB:** PostgreSQL via `pgx/v5` (connection pool).
- **Passwords:** `golang.org/x/crypto/bcrypt`.
- **JWT:** `github.com/golang-jwt/jwt/v5` (HMAC-SHA256).
- **Config:** Env vars `PORT`, `DATABASE_URL`, `JWT_SECRET`.

## API

| Method | Path | Body | Response |
|--------|------|------|----------|
| POST | `/api/auth/register` | `{"email":"...","password":"..."}` | 201 `{"id","email","token","expires_at"}` or 400/409 |
| POST | `/api/auth/login` | `{"email":"...","password":"..."}` | 200 `{"token","expires_at"}` or 401 |
| GET | `/api/auth/me` | — (Bearer token) | 200 `{"id","email"}` or 401 |

## Data Model

- **users:** `id` (UUID), `email` (unique), `password_hash`, `created_at`.

## Security

- Passwords hashed with bcrypt; never logged or returned.
- JWT expiry configurable (e.g. 24h).
- CORS configurable for MAUI client.
- HTTPS in production (handled outside this service).

## Project Layout

- `cmd/server/main.go` — entrypoint.
- `internal/config` — load from env.
- `internal/db` — pgx pool, migrations (users table).
- `internal/models` — User struct.
- `internal/auth` — JWT issue/validate, password hash/compare.
- `internal/handlers` — register, login, me.
