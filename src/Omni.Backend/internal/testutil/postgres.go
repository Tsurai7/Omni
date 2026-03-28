// Package testutil provides shared test helpers for integration tests.
// Use build tag `//go:build integration` on files that call these helpers.
package testutil

import (
	"context"
	"testing"
	"time"

	"omni-backend/internal/db"

	"github.com/jackc/pgx/v5/pgxpool"
	"github.com/stretchr/testify/require"
	"github.com/testcontainers/testcontainers-go"
	"github.com/testcontainers/testcontainers-go/modules/postgres"
	"github.com/testcontainers/testcontainers-go/wait"
)

// PostgresPool starts a real Postgres container and returns a connection pool
// with all migrations applied. The container is terminated on test cleanup.
// Expensive to start — use t.Parallel() + TestMain caching for large test suites.
func PostgresPool(t *testing.T) *pgxpool.Pool {
	t.Helper()
	ctx := context.Background()

	pgContainer, err := postgres.Run(ctx, "postgres:16-alpine",
		postgres.WithDatabase("omni_test"),
		postgres.WithUsername("omni"),
		postgres.WithPassword("omni"),
		testcontainers.WithWaitStrategy(
			wait.ForLog("database system is ready to accept connections").
				WithOccurrence(2).
				WithStartupTimeout(60*time.Second),
		),
	)
	require.NoError(t, err, "start postgres container")

	t.Cleanup(func() {
		if err := pgContainer.Terminate(ctx); err != nil {
			t.Logf("failed to terminate postgres container: %v", err)
		}
	})

	connStr, err := pgContainer.ConnectionString(ctx, "sslmode=disable")
	require.NoError(t, err, "get postgres connection string")

	pool, err := pgxpool.New(ctx, connStr)
	require.NoError(t, err, "create pgx pool")

	t.Cleanup(pool.Close)

	// Run all schema migrations on the test database.
	require.NoError(t, db.Migrate(ctx, pool), "run migrations")

	return pool
}

// TruncateTables truncates all application tables between tests for isolation.
// Call this in t.Cleanup or between sub-tests.
func TruncateTables(t *testing.T, pool *pgxpool.Pool) {
	t.Helper()
	ctx := context.Background()
	_, err := pool.Exec(ctx, `
		TRUNCATE
			chat_messages,
			chat_conversations,
			daily_focus_scores,
			user_notifications,
			calendar_events,
			user_google_tokens,
			tasks,
			sessions,
			usage_records,
			users
		RESTART IDENTITY CASCADE
	`)
	require.NoError(t, err, "truncate tables")
}
