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
	duration_seconds BIGINT NOT NULL,
	intention TEXT,
	subjective_rating INT,
	reflection_note TEXT
);
CREATE INDEX IF NOT EXISTS idx_sessions_user_started ON sessions(user_id, started_at);
`

// Older deployments created sessions before these columns existed; CREATE TABLE IF NOT EXISTS does not add them.
const alterSessionsAddIntention = `ALTER TABLE sessions ADD COLUMN IF NOT EXISTS intention TEXT`
const alterSessionsAddSubjectiveRating = `ALTER TABLE sessions ADD COLUMN IF NOT EXISTS subjective_rating INT`
const alterSessionsAddReflectionNote = `ALTER TABLE sessions ADD COLUMN IF NOT EXISTS reflection_note TEXT`

const createTableTasks = `
CREATE TABLE IF NOT EXISTS tasks (
	id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
	user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
	title TEXT NOT NULL,
	status TEXT NOT NULL DEFAULT 'pending',
	priority TEXT NOT NULL DEFAULT 'medium',
	created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
	updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_tasks_user_id ON tasks(user_id);
`

const alterTasksAddPriority = `ALTER TABLE tasks ADD COLUMN IF NOT EXISTS priority TEXT NOT NULL DEFAULT 'medium'`

const alterTasksAddDueDate = `ALTER TABLE tasks ADD COLUMN IF NOT EXISTS due_date TIMESTAMPTZ`
const alterTasksAddScheduledFor = `ALTER TABLE tasks ADD COLUMN IF NOT EXISTS scheduled_for TIMESTAMPTZ`
const alterTasksAddGoogleEventID = `ALTER TABLE tasks ADD COLUMN IF NOT EXISTS google_event_id TEXT`

const createTableUserGoogleTokens = `
CREATE TABLE IF NOT EXISTS user_google_tokens (
	user_id       UUID PRIMARY KEY REFERENCES users(id) ON DELETE CASCADE,
	access_token  TEXT NOT NULL,
	refresh_token TEXT NOT NULL,
	expires_at    TIMESTAMPTZ NOT NULL,
	email         TEXT,
	connected_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
`

const createTableCalendarEvents = `
CREATE TABLE IF NOT EXISTS calendar_events (
	id                 UUID PRIMARY KEY DEFAULT gen_random_uuid(),
	user_id            UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
	google_event_id    TEXT,
	title              TEXT NOT NULL,
	description        TEXT,
	start_at           TIMESTAMPTZ NOT NULL,
	end_at             TIMESTAMPTZ,
	is_all_day         BOOLEAN NOT NULL DEFAULT FALSE,
	google_calendar_id TEXT,
	color              TEXT,
	omni_task_id       UUID REFERENCES tasks(id) ON DELETE SET NULL,
	last_synced_at     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
	created_at         TIMESTAMPTZ NOT NULL DEFAULT NOW(),
	UNIQUE (user_id, google_event_id)
);
CREATE INDEX IF NOT EXISTS idx_calendar_events_user_start ON calendar_events(user_id, start_at);
`

const createTableUserNotifications = `
CREATE TABLE IF NOT EXISTS user_notifications (
	id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
	user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
	created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
	type TEXT NOT NULL,
	title TEXT,
	body TEXT,
	action_type TEXT,
	action_payload JSONB,
	read_at TIMESTAMPTZ
);
CREATE INDEX IF NOT EXISTS idx_user_notifications_user_created ON user_notifications(user_id, created_at DESC);
`

const createTableDailyFocusScores = `
CREATE TABLE IF NOT EXISTS daily_focus_scores (
	id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
	user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
	score_date DATE NOT NULL,
	score INT NOT NULL,
	breakdown JSONB,
	computed_at TIMESTAMPTZ DEFAULT NOW(),
	UNIQUE (user_id, score_date)
);
CREATE INDEX IF NOT EXISTS idx_daily_focus_scores_user_date ON daily_focus_scores(user_id, score_date DESC);
`

const createTableChatConversations = `
CREATE TABLE IF NOT EXISTS chat_conversations (
	id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
	user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
	title TEXT NOT NULL DEFAULT '',
	created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
	last_message_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
	deleted_at TIMESTAMPTZ
);
CREATE INDEX IF NOT EXISTS idx_chat_conv_user ON chat_conversations(user_id, last_message_at DESC) WHERE deleted_at IS NULL;
`

const createTableChatMessages = `
CREATE TABLE IF NOT EXISTS chat_messages (
	id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
	conversation_id UUID NOT NULL REFERENCES chat_conversations(id) ON DELETE CASCADE,
	user_id UUID NOT NULL,
	role TEXT NOT NULL CHECK (role IN ('user', 'assistant')),
	content TEXT NOT NULL,
	metadata JSONB,
	created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_chat_msg_conv ON chat_messages(conversation_id, created_at);
`

func Migrate(ctx context.Context, pool *pgxpool.Pool) error {
	if _, err := pool.Exec(ctx, createTableUsers); err != nil {
		return err
	}
	if _, err := pool.Exec(ctx, createTableUsageRecords); err != nil {
		return err
	}
	if _, err := pool.Exec(ctx, createTableSessions); err != nil {
		return err
	}
	if _, err := pool.Exec(ctx, alterSessionsAddIntention); err != nil {
		return err
	}
	if _, err := pool.Exec(ctx, alterSessionsAddSubjectiveRating); err != nil {
		return err
	}
	if _, err := pool.Exec(ctx, alterSessionsAddReflectionNote); err != nil {
		return err
	}
	if _, err := pool.Exec(ctx, createTableTasks); err != nil {
		return err
	}
	if _, err := pool.Exec(ctx, alterTasksAddPriority); err != nil {
		return err
	}
	if _, err := pool.Exec(ctx, alterTasksAddDueDate); err != nil {
		return err
	}
	if _, err := pool.Exec(ctx, alterTasksAddScheduledFor); err != nil {
		return err
	}
	if _, err := pool.Exec(ctx, alterTasksAddGoogleEventID); err != nil {
		return err
	}
	if _, err := pool.Exec(ctx, createTableUserGoogleTokens); err != nil {
		return err
	}
	if _, err := pool.Exec(ctx, createTableCalendarEvents); err != nil {
		return err
	}
	if _, err := pool.Exec(ctx, createTableUserNotifications); err != nil {
		return err
	}
	if _, err := pool.Exec(ctx, createTableDailyFocusScores); err != nil {
		return err
	}
	if _, err := pool.Exec(ctx, createTableChatConversations); err != nil {
		return err
	}
	_, err := pool.Exec(ctx, createTableChatMessages)
	return err
}
