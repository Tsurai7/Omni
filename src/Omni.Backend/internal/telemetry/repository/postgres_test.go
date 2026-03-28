//go:build integration

package repository_test

import (
	"context"
	"testing"
	"time"

	tdomain "omni-backend/internal/telemetry/domain"
	"omni-backend/internal/telemetry/repository"
	"omni-backend/internal/testutil"

	"github.com/google/uuid"
	"github.com/jackc/pgx/v5/pgxpool"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
)

func seedUser(t *testing.T, pool *pgxpool.Pool, userID uuid.UUID) {
	t.Helper()
	_, err := pool.Exec(context.Background(),
		`INSERT INTO users (id, email, password_hash) VALUES ($1, $2, 'hash')`,
		userID, "teltest+"+userID.String()[:8]+"@test.com")
	require.NoError(t, err)
}

// ---- UsageRepository ----

func TestPostgresUsageRepo_BulkInsert(t *testing.T) {
	pool := testutil.PostgresPool(t)
	repo := repository.NewPostgresUsage(pool)
	ctx := context.Background()

	userID := uuid.New()
	seedUser(t, pool, userID)
	t.Cleanup(func() { testutil.TruncateTables(t, pool) })

	now := time.Now().UTC().Truncate(time.Second)

	tests := []struct {
		name    string
		records []*tdomain.UsageRecord
		wantErr bool
	}{
		{
			name: "happy path — inserts multiple records",
			records: []*tdomain.UsageRecord{
				{ID: uuid.New(), UserID: userID, AppName: "VSCode", Category: "Coding", DurationSeconds: 3600, RecordedAt: now},
				{ID: uuid.New(), UserID: userID, AppName: "Chrome", Category: "Research", DurationSeconds: 600, RecordedAt: now},
			},
		},
		{
			name: "happy path — single record",
			records: []*tdomain.UsageRecord{
				{ID: uuid.New(), UserID: userID, AppName: "Slack", Category: "Communication", DurationSeconds: 1200, RecordedAt: now},
			},
		},
		{
			name:    "corner case — empty slice is no-op",
			records: []*tdomain.UsageRecord{},
		},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			err := repo.BulkInsert(ctx, tt.records)
			if tt.wantErr {
				assert.Error(t, err)
				return
			}
			require.NoError(t, err)
		})
	}
}

func TestPostgresUsageRepo_List(t *testing.T) {
	pool := testutil.PostgresPool(t)
	repo := repository.NewPostgresUsage(pool)
	ctx := context.Background()

	userA := uuid.New()
	userB := uuid.New()
	seedUser(t, pool, userA)
	seedUser(t, pool, userB)
	t.Cleanup(func() { testutil.TruncateTables(t, pool) })

	today := time.Now().UTC().Truncate(24 * time.Hour)
	records := []*tdomain.UsageRecord{
		{ID: uuid.New(), UserID: userA, AppName: "VSCode", Category: "Coding", DurationSeconds: 3600, RecordedAt: today},
		{ID: uuid.New(), UserID: userA, AppName: "Chrome", Category: "Research", DurationSeconds: 600, RecordedAt: today},
		{ID: uuid.New(), UserID: userB, AppName: "App", Category: "Other", DurationSeconds: 300, RecordedAt: today},
	}
	require.NoError(t, repo.BulkInsert(ctx, records))

	dateStr := today.Format("2006-01-02")

	tests := []struct {
		name      string
		query     tdomain.UsageQuery
		wantCount int
	}{
		{
			name:      "happy path — returns only userA entries",
			query:     tdomain.UsageQuery{UserID: userA, From: dateStr, To: dateStr, GroupBy: "day"},
			wantCount: 2,
		},
		{
			name:      "happy path — filter by category",
			query:     tdomain.UsageQuery{UserID: userA, From: dateStr, To: dateStr, GroupBy: "day", CategoryFilter: "Coding"},
			wantCount: 1,
		},
		{
			name:      "corner case — user with no records returns empty",
			query:     tdomain.UsageQuery{UserID: uuid.New(), From: dateStr, To: dateStr, GroupBy: "day"},
			wantCount: 0,
		},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			result, err := repo.List(ctx, tt.query)
			require.NoError(t, err)
			assert.Len(t, result, tt.wantCount)
		})
	}
}

// ---- SessionRepository ----

func TestPostgresSessionRepo_BulkInsert(t *testing.T) {
	pool := testutil.PostgresPool(t)
	repo := repository.NewPostgresSession(pool)
	ctx := context.Background()

	userID := uuid.New()
	seedUser(t, pool, userID)
	t.Cleanup(func() { testutil.TruncateTables(t, pool) })

	startedAt := time.Now().UTC().Truncate(time.Second)

	tests := []struct {
		name     string
		sessions []*tdomain.Session
	}{
		{
			name: "happy path — inserts sessions",
			sessions: []*tdomain.Session{
				{ID: uuid.New(), UserID: userID, Name: "Deep Work", ActivityType: "focus", StartedAt: startedAt, DurationSeconds: 3600},
				{ID: uuid.New(), UserID: userID, Name: "Meeting", ActivityType: "collaboration", StartedAt: startedAt, DurationSeconds: 1800},
			},
		},
		{
			name:     "corner case — empty slice is no-op",
			sessions: []*tdomain.Session{},
		},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			require.NoError(t, repo.BulkInsert(ctx, tt.sessions))
		})
	}
}

// ---- NotificationRepository ----

func TestPostgresNotifRepo_MarkRead(t *testing.T) {
	pool := testutil.PostgresPool(t)
	repo := repository.NewPostgresNotification(pool)
	ctx := context.Background()

	userA := uuid.New()
	userB := uuid.New()
	seedUser(t, pool, userA)
	seedUser(t, pool, userB)
	t.Cleanup(func() { testutil.TruncateTables(t, pool) })

	// Seed a notification.
	var notifID uuid.UUID
	err := pool.QueryRow(ctx,
		`INSERT INTO user_notifications (user_id, type, title, body, action_type)
		 VALUES ($1, 'burnout', 'Take a break', 'You have been working for 2h', 'take_break')
		 RETURNING id`,
		userA).Scan(&notifID)
	require.NoError(t, err)

	tests := []struct {
		name    string
		id      uuid.UUID
		userID  uuid.UUID
		wantErr error
	}{
		{
			name: "happy path — notification marked as read",
			id:   notifID, userID: userA,
		},
		{
			name: "corner case — non-existent id returns ErrNotificationNotFound",
			id:   uuid.New(), userID: userA,
			wantErr: tdomain.ErrNotificationNotFound,
		},
		{
			name: "corner case — other user cannot mark read (returns ErrNotificationNotFound)",
			id:   notifID, userID: userB,
			wantErr: tdomain.ErrNotificationNotFound,
		},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			err := repo.MarkRead(ctx, tt.id, tt.userID)
			if tt.wantErr != nil {
				assert.ErrorIs(t, err, tt.wantErr)
				return
			}
			require.NoError(t, err)
		})
	}
}
