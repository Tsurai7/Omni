//go:build integration

package repository_test

import (
	"context"
	"testing"
	"time"

	"omni-backend/internal/profile/domain"
	"omni-backend/internal/profile/repository"
	"omni-backend/internal/testutil"

	"github.com/google/uuid"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
)

func newUser() *domain.User {
	return &domain.User{
		ID:           uuid.New(),
		Email:        "user+" + uuid.New().String()[:8] + "@example.com",
		PasswordHash: "$2a$10$hashhashhashhashhashhashhashhashhashhashhashhash",
		CreatedAt:    time.Now().UTC().Truncate(time.Microsecond),
	}
}

// ---- Create ----

func TestPostgresUserRepo_Create(t *testing.T) {
	pool := testutil.PostgresPool(t)
	repo := repository.NewPostgres(pool)
	ctx := context.Background()
	t.Cleanup(func() { testutil.TruncateTables(t, pool) })

	tests := []struct {
		name    string
		user    func() *domain.User
		wantErr error
	}{
		{
			name: "happy path — user created and retrievable by email",
			user: newUser,
		},
		{
			name: "corner case — duplicate email returns ErrEmailAlreadyTaken",
			user: func() *domain.User {
				u := newUser()
				require.NoError(t, repo.Create(ctx, u)) // pre-insert
				u2 := newUser()
				u2.Email = u.Email // same email, new UUID
				return u2
			},
			wantErr: domain.ErrEmailAlreadyTaken,
		},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			user := tt.user()
			err := repo.Create(ctx, user)

			if tt.wantErr != nil {
				assert.ErrorIs(t, err, tt.wantErr)
				return
			}

			require.NoError(t, err)

			got, err := repo.GetByEmail(ctx, user.Email)
			require.NoError(t, err)
			assert.Equal(t, user.ID, got.ID)
			assert.Equal(t, user.Email, got.Email)
			assert.Equal(t, user.PasswordHash, got.PasswordHash)
		})
	}
}

// ---- GetByEmail ----

func TestPostgresUserRepo_GetByEmail(t *testing.T) {
	pool := testutil.PostgresPool(t)
	repo := repository.NewPostgres(pool)
	ctx := context.Background()
	t.Cleanup(func() { testutil.TruncateTables(t, pool) })

	user := newUser()
	require.NoError(t, repo.Create(ctx, user))

	tests := []struct {
		name    string
		email   string
		wantErr error
	}{
		{
			name:  "happy path — existing user returned",
			email: user.Email,
		},
		{
			name:    "corner case — unknown email returns ErrUserNotFound",
			email:   "nobody@example.com",
			wantErr: domain.ErrUserNotFound,
		},
		{
			name:    "corner case — case-sensitive miss returns ErrUserNotFound",
			email:   "NOBODY@EXAMPLE.COM",
			wantErr: domain.ErrUserNotFound,
		},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			got, err := repo.GetByEmail(ctx, tt.email)
			if tt.wantErr != nil {
				assert.ErrorIs(t, err, tt.wantErr)
				assert.Nil(t, got)
				return
			}
			require.NoError(t, err)
			assert.Equal(t, user.ID, got.ID)
		})
	}
}

// ---- GetByID ----

func TestPostgresUserRepo_GetByID(t *testing.T) {
	pool := testutil.PostgresPool(t)
	repo := repository.NewPostgres(pool)
	ctx := context.Background()
	t.Cleanup(func() { testutil.TruncateTables(t, pool) })

	user := newUser()
	require.NoError(t, repo.Create(ctx, user))

	tests := []struct {
		name    string
		id      uuid.UUID
		wantErr error
	}{
		{
			name: "happy path — existing user returned by ID",
			id:   user.ID,
		},
		{
			name:    "corner case — unknown ID returns ErrUserNotFound",
			id:      uuid.New(),
			wantErr: domain.ErrUserNotFound,
		},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			got, err := repo.GetByID(ctx, tt.id)
			if tt.wantErr != nil {
				assert.ErrorIs(t, err, tt.wantErr)
				return
			}
			require.NoError(t, err)
			assert.Equal(t, user.Email, got.Email)
		})
	}
}
