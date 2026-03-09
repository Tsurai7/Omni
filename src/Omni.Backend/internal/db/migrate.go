package db

import (
	"context"

	"github.com/jackc/pgx/v5/pgxpool"
)

const createTableUsers = `
CREATE TABLE IF NOT EXISTS users (
	id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
	email TEXT UNIQUE NOT NULL,
	password_hash TEXT NOT NULL,
	created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_users_email ON users(email);
`

const createTableUsageRecords = `
CREATE TABLE IF NOT EXISTS usage_records (
	id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
	user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
	app_name TEXT NOT NULL,
	category TEXT NOT NULL,
	duration_seconds BIGINT NOT NULL,
	recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_usage_records_user_recorded ON usage_records(user_id, recorded_at);
`

const createTableSessions = `
CREATE TABLE IF NOT EXISTS sessions (
	id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
	user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
	name TEXT NOT NULL,
	activity_type TEXT NOT NULL DEFAULT 'other',
	started_at TIMESTAMPTZ NOT NULL,
	duration_seconds BIGINT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_sessions_user_started ON sessions(user_id, started_at);
`

func Migrate(ctx context.Context, pool *pgxpool.Pool) error {
	if _, err := pool.Exec(ctx, createTableUsers); err != nil {
		return err
	}
	if _, err := pool.Exec(ctx, createTableUsageRecords); err != nil {
		return err
	}
	_, err := pool.Exec(ctx, createTableSessions)
	return err
}
