# Auth Backend Implementation Plan

> **For Claude:** Implement this plan task-by-task in `src/Omni.Backend`.

**Goal:** Implement authorization and account creation with Go, Gin, and PostgreSQL in `src/Omni.Backend`.

**Architecture:** Gin HTTP server; pgx pool for PostgreSQL; bcrypt for passwords; JWT for auth; config from env.

**Tech Stack:** Go 1.21+, Gin, pgx/v5, golang.org/x/crypto/bcrypt, github.com/golang-jwt/jwt/v5.

---

### Task 1: Go module and project layout

**Files:**
- Create: `src/Omni.Backend/go.mod`
- Create: `src/Omni.Backend/cmd/server/main.go` (stub)
- Create: `src/Omni.Backend/internal/config/config.go`
- Create: `src/Omni.Backend/.env.example`

**Steps:**
1. Create directory `src/Omni.Backend`.
2. Run `go mod init omni-backend` (or module path matching repo).
3. Add `go.mod` with module path (e.g. `github.com/tsurai/omni/backend` or `omni-backend`).
4. Create `internal/config/config.go`: load `PORT` (default 8080), `DATABASE_URL`, `JWT_SECRET` from os.Getenv; validate required (DATABASE_URL, JWT_SECRET).
5. Create `cmd/server/main.go`: read config, log and exit if invalid; otherwise start Gin on PORT (placeholder route).
6. Create `.env.example` with PORT, DATABASE_URL, JWT_SECRET.
7. Run `go mod tidy` and `go build ./cmd/server` to verify.

---

### Task 2: Database connection and users table

**Files:**
- Create: `src/Omni.Backend/internal/db/pool.go`
- Create: `src/Omni.Backend/internal/db/migrate.go`
- Create: `src/Omni.Backend/internal/models/user.go`

**Steps:**
1. `go get github.com/jackc/pgx/v5 pgxpool`.
2. `internal/models/user.go`: struct User with Id (uuid.UUID), Email, PasswordHash, CreatedAt (time.Time).
3. `internal/db/pool.go`: NewPool(ctx, connString) returning *pgxpool.Pool; Close().
4. `internal/db/migrate.go`: CreateTableUsers(ctx, pool) — CREATE TABLE IF NOT EXISTS users (id UUID PRIMARY KEY DEFAULT gen_random_uuid(), email TEXT UNIQUE NOT NULL, password_hash TEXT NOT NULL, created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()).
5. In main, after config: create pool, run migrate, defer pool.Close().
6. Build and run with valid DATABASE_URL to confirm table creation.

---

### Task 3: Auth package (password + JWT)

**Files:**
- Create: `src/Omni.Backend/internal/auth/password.go`
- Create: `src/Omni.Backend/internal/auth/jwt.go`

**Steps:**
1. `go get golang.org/x/crypto/bcrypt github.com/golang-jwt/jwt/v5 github.com/google/uuid`.
2. `password.go`: HashPassword(plain string) (string, error) using bcrypt.DefaultCost; ComparePassword(hash, plain string) error.
3. `jwt.go`: Claims struct with jwt.RegisteredClaims + UserID (string UUID), Email. GenerateToken(userID, email, secret string, expiry time.Duration) (string, time.Time, error). ValidateToken(tokenString, secret string) (*Claims, error).
4. Add JWT_EXPIRY_HOURS to config (default 24). Use in GenerateToken.
5. Build to verify.

---

### Task 4: Handlers (register, login, me)

**Files:**
- Create: `src/Omni.Backend/internal/handlers/auth.go`
- Create: `src/Omni.Backend/internal/middleware/auth.go` (optional: JWT middleware)

**Steps:**
1. Define request/response DTOs in handlers or a separate file: RegisterRequest (Email, Password), LoginRequest (Email, Password), TokenResponse (Token, ExpiresAt), UserResponse (Id, Email), MeResponse same as UserResponse.
2. Register: bind JSON; validate email format and password length; hash password; insert user (id from uuid.New()); on unique violation return 409; generate JWT; return 201 with user + token + expires_at.
3. Login: bind JSON; query user by email; if not found return 401; compare password; if invalid 401; generate JWT; return 200 with token + expires_at.
4. Me: extract Bearer token from Authorization header; validate JWT; return 200 with id + email or 401.
5. Middleware: AuthRequired(secret) returns Gin handler that parses Bearer token, validates, sets claims in context (or handler can parse in Me). Use middleware on GET /api/auth/me.
6. Build to verify.

---

### Task 5: Wire routes and CORS

**Files:**
- Modify: `src/Omni.Backend/cmd/server/main.go`

**Steps:**
1. Create Gin router; apply CORS middleware (AllowOrigins "*" for dev, or configurable).
2. Group `/api/auth`: POST `/register` -> RegisterHandler, POST `/login` -> LoginHandler, GET `/me` -> AuthRequired -> MeHandler.
3. Inject config, pool, and auth helpers into handlers (via struct or closure).
4. Run router.Run(":" + config.Port).
5. Manual test: curl register, curl login, curl -H "Authorization: Bearer <token>" /api/auth/me.
6. Add README.md with run instructions and env vars.

---

## Execution

Implement Tasks 1–5 in order. After each task, run `go build ./cmd/server` (and where applicable, run server and smoke-test with curl).
