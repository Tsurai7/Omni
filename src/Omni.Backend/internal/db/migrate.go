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

func Migrate(ctx context.Context, pool *pgxpool.Pool) error {
	if _, err := pool.Exec(ctx, createTableUsers); err != nil {
		return err
	}
	_, err := pool.Exec(ctx, createTableUsageRecords)
	return err
}
