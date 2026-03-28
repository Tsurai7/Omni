//go:build integration

package repository_test

import (
	"context"
	"testing"
	"time"

	"omni-backend/internal/task/domain"
	"omni-backend/internal/task/repository"
	"omni-backend/internal/testutil"

	"github.com/google/uuid"
	"github.com/jackc/pgx/v5/pgxpool"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
)

// seedUser inserts a bare user row so task FK constraints pass.
func seedUser(t *testing.T, pool *pgxpool.Pool, userID uuid.UUID) {
	t.Helper()
	_, err := pool.Exec(context.Background(),
		`INSERT INTO users (id, email, password_hash) VALUES ($1, $2, 'hash')`,
		userID, "user+"+userID.String()[:8]+"@test.com")
	require.NoError(t, err)
}

func newTask(userID uuid.UUID) *domain.Task {
	now := time.Now().UTC().Truncate(time.Microsecond)
	return &domain.Task{
		ID:        uuid.New(),
		UserID:    userID,
		Title:     "Integration Test Task",
		Status:    domain.StatusPending,
		Priority:  domain.PriorityMedium,
		CreatedAt: now,
		UpdatedAt: now,
	}
}

// ---- Create ----

func TestPostgresTaskRepo_Create(t *testing.T) {
	pool := testutil.PostgresPool(t)
	repo := repository.NewPostgres(pool)
	ctx := context.Background()

	userID := uuid.New()
	seedUser(t, pool, userID)
	t.Cleanup(func() { testutil.TruncateTables(t, pool) })

	due := time.Now().Add(24 * time.Hour).UTC().Truncate(time.Microsecond)

	tests := []struct {
		name    string
		task    func() *domain.Task
		wantErr bool
	}{
		{
			name: "happy path — task persisted and retrievable",
			task: func() *domain.Task { return newTask(userID) },
		},
		{
			name: "happy path — task with due date persisted",
			task: func() *domain.Task {
				t := newTask(userID)
				t.DueDate = &due
				return t
			},
		},
		{
			name: "happy path — task with nil due date",
			task: func() *domain.Task {
				t := newTask(userID)
				t.DueDate = nil
				return t
			},
		},
		{
			name: "corner case — duplicate ID returns error",
			task: func() *domain.Task {
				task := newTask(userID)
				require.NoError(t, repo.Create(ctx, task)) // pre-insert
				return task                                // same ID → conflict
			},
			wantErr: true,
		},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			task := tt.task()
			err := repo.Create(ctx, task)

			if tt.wantErr {
				assert.Error(t, err)
				return
			}

			require.NoError(t, err)

			// Verify round-trip integrity.
			got, err := repo.GetByID(ctx, task.ID, task.UserID)
			require.NoError(t, err)
			assert.Equal(t, task.Title, got.Title)
			assert.Equal(t, task.Status, got.Status)
			assert.Equal(t, task.Priority, got.Priority)
			if task.DueDate != nil {
				require.NotNil(t, got.DueDate)
				assert.WithinDuration(t, *task.DueDate, *got.DueDate, time.Second)
			} else {
				assert.Nil(t, got.DueDate)
			}
		})
	}
}

// ---- GetByID ----

func TestPostgresTaskRepo_GetByID(t *testing.T) {
	pool := testutil.PostgresPool(t)
	repo := repository.NewPostgres(pool)
	ctx := context.Background()

	userA := uuid.New()
	userB := uuid.New()
	seedUser(t, pool, userA)
	seedUser(t, pool, userB)
	t.Cleanup(func() { testutil.TruncateTables(t, pool) })

	task := newTask(userA)
	require.NoError(t, repo.Create(ctx, task))

	tests := []struct {
		name    string
		id      uuid.UUID
		userID  uuid.UUID
		wantErr error
	}{
		{
			name: "happy path — owner retrieves task",
			id:   task.ID, userID: userA,
		},
		{
			name: "corner case — non-existent ID returns ErrTaskNotFound",
			id:   uuid.New(), userID: userA,
			wantErr: domain.ErrTaskNotFound,
		},
		{
			name: "corner case — different user cannot access task",
			id:   task.ID, userID: userB,
			wantErr: domain.ErrTaskNotFound,
		},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			got, err := repo.GetByID(ctx, tt.id, tt.userID)
			if tt.wantErr != nil {
				assert.ErrorIs(t, err, tt.wantErr)
				assert.Nil(t, got)
				return
			}
			require.NoError(t, err)
			assert.Equal(t, task.ID, got.ID)
		})
	}
}

// ---- ListByUser ----

func TestPostgresTaskRepo_ListByUser(t *testing.T) {
	pool := testutil.PostgresPool(t)
	repo := repository.NewPostgres(pool)
	ctx := context.Background()

	userA := uuid.New()
	userB := uuid.New()
	seedUser(t, pool, userA)
	seedUser(t, pool, userB)
	t.Cleanup(func() { testutil.TruncateTables(t, pool) })

	// Seed: 2 tasks for userA, 1 for userB.
	taskA1 := newTask(userA)
	taskA2 := newTask(userA)
	taskB := newTask(userB)
	require.NoError(t, repo.Create(ctx, taskA1))
	require.NoError(t, repo.Create(ctx, taskA2))
	require.NoError(t, repo.Create(ctx, taskB))

	tests := []struct {
		name      string
		userID    uuid.UUID
		wantCount int
	}{
		{
			name:      "happy path — returns only userA tasks",
			userID:    userA,
			wantCount: 2,
		},
		{
			name:      "happy path — userB sees only their task",
			userID:    userB,
			wantCount: 1,
		},
		{
			name:      "corner case — user with no tasks returns empty slice",
			userID:    uuid.New(),
			wantCount: 0,
		},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			tasks, err := repo.ListByUser(ctx, tt.userID)
			require.NoError(t, err)
			assert.Len(t, tasks, tt.wantCount)
		})
	}
}

// ---- Update ----

func TestPostgresTaskRepo_Update(t *testing.T) {
	pool := testutil.PostgresPool(t)
	repo := repository.NewPostgres(pool)
	ctx := context.Background()

	userID := uuid.New()
	seedUser(t, pool, userID)
	t.Cleanup(func() { testutil.TruncateTables(t, pool) })

	tests := []struct {
		name    string
		setup   func() *domain.Task
		mutate  func(*domain.Task)
		wantErr error
		check   func(*testing.T, *domain.Task)
	}{
		{
			name: "happy path — title and priority updated",
			setup: func() *domain.Task {
				t := newTask(userID)
				require.NoError(t, repo.Create(ctx, t))
				return t
			},
			mutate: func(t *domain.Task) {
				t.Title = "Updated Title"
				t.Priority = domain.PriorityHigh
			},
			check: func(t *testing.T, got *domain.Task) {
				assert.Equal(t, "Updated Title", got.Title)
				assert.Equal(t, domain.PriorityHigh, got.Priority)
			},
		},
		{
			name: "happy path — status updated to done",
			setup: func() *domain.Task {
				t := newTask(userID)
				require.NoError(t, repo.Create(ctx, t))
				return t
			},
			mutate: func(t *domain.Task) { t.Status = domain.StatusDone },
			check: func(t *testing.T, got *domain.Task) {
				assert.Equal(t, domain.StatusDone, got.Status)
			},
		},
		{
			name: "corner case — updating non-existent task returns ErrTaskNotFound",
			setup: func() *domain.Task {
				return newTask(userID) // not inserted
			},
			mutate:  func(t *domain.Task) {},
			wantErr: domain.ErrTaskNotFound,
		},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			task := tt.setup()
			tt.mutate(task)

			err := repo.Update(ctx, task)
			if tt.wantErr != nil {
				assert.ErrorIs(t, err, tt.wantErr)
				return
			}

			require.NoError(t, err)
			got, err := repo.GetByID(ctx, task.ID, task.UserID)
			require.NoError(t, err)
			if tt.check != nil {
				tt.check(t, got)
			}
		})
	}
}

// ---- Delete ----

func TestPostgresTaskRepo_Delete(t *testing.T) {
	pool := testutil.PostgresPool(t)
	repo := repository.NewPostgres(pool)
	ctx := context.Background()

	userA := uuid.New()
	userB := uuid.New()
	seedUser(t, pool, userA)
	seedUser(t, pool, userB)
	t.Cleanup(func() { testutil.TruncateTables(t, pool) })

	tests := []struct {
		name    string
		setup   func() (id, userID uuid.UUID)
		wantErr error
	}{
		{
			name: "happy path — deletes own task",
			setup: func() (uuid.UUID, uuid.UUID) {
				task := newTask(userA)
				require.NoError(t, repo.Create(ctx, task))
				return task.ID, task.UserID
			},
		},
		{
			name: "corner case — non-existent ID returns ErrTaskNotFound",
			setup: func() (uuid.UUID, uuid.UUID) {
				return uuid.New(), userA
			},
			wantErr: domain.ErrTaskNotFound,
		},
		{
			name: "corner case — different user's task returns ErrTaskNotFound",
			setup: func() (uuid.UUID, uuid.UUID) {
				task := newTask(userA)
				require.NoError(t, repo.Create(ctx, task))
				return task.ID, userB // userB tries to delete userA's task
			},
			wantErr: domain.ErrTaskNotFound,
		},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			id, userID := tt.setup()

			err := repo.Delete(ctx, id, userID)
			if tt.wantErr != nil {
				assert.ErrorIs(t, err, tt.wantErr)
				return
			}

			require.NoError(t, err)
			_, err = repo.GetByID(ctx, id, userID)
			assert.ErrorIs(t, err, domain.ErrTaskNotFound)
		})
	}
}
