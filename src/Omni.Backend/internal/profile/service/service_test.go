package service_test

import (
	"context"
	"log/slog"
	"testing"
	"time"

	"omni-backend/internal/auth"
	"omni-backend/internal/profile/domain"
	"omni-backend/internal/profile/service"

	"github.com/google/uuid"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/mock"
	"github.com/stretchr/testify/require"
)

// ---- mock repository ----

type mockUserRepo struct{ mock.Mock }

func (m *mockUserRepo) GetByEmail(ctx context.Context, email string) (*domain.User, error) {
	args := m.Called(ctx, email)
	if args.Get(0) == nil {
		return nil, args.Error(1)
	}
	return args.Get(0).(*domain.User), args.Error(1)
}

func (m *mockUserRepo) GetByID(ctx context.Context, id uuid.UUID) (*domain.User, error) {
	args := m.Called(ctx, id)
	if args.Get(0) == nil {
		return nil, args.Error(1)
	}
	return args.Get(0).(*domain.User), args.Error(1)
}

func (m *mockUserRepo) Create(ctx context.Context, u *domain.User) error {
	return m.Called(ctx, u).Error(0)
}

// ---- helpers ----

func newSvc(repo *mockUserRepo) service.AuthService {
	return service.New(repo, "test-secret-32chars-xxxxxxxxxxxxxxxxx", 24*time.Hour, slog.Default())
}

func hashedPassword(t *testing.T, plain string) string {
	t.Helper()
	h, err := auth.HashPassword(plain)
	require.NoError(t, err)
	return h
}

// ---- Register tests ----

func TestRegister(t *testing.T) {
	tests := []struct {
		name      string
		cmd       service.RegisterCmd
		setupMock func(*mockUserRepo)
		wantErr   error
		check     func(*testing.T, *service.RegisterResult)
	}{
		{
			name: "happy path — new user gets token",
			cmd:  service.RegisterCmd{Email: "alice@example.com", Password: "Secure123!"},
			setupMock: func(r *mockUserRepo) {
				r.On("Create", mock.Anything, mock.MatchedBy(func(u *domain.User) bool {
					return u.Email == "alice@example.com" &&
						u.PasswordHash != "" &&
						u.PasswordHash != "Secure123!" // must be hashed
				})).Return(nil)
			},
			check: func(t *testing.T, res *service.RegisterResult) {
				assert.Equal(t, "alice@example.com", res.User.Email)
				assert.NotEmpty(t, res.Token)
				assert.True(t, res.ExpiresAt.After(time.Now()))
			},
		},
		{
			name: "happy path — email is lowercased and trimmed",
			cmd:  service.RegisterCmd{Email: "  ALICE@EXAMPLE.COM  ", Password: "Password1!"},
			setupMock: func(r *mockUserRepo) {
				r.On("Create", mock.Anything, mock.MatchedBy(func(u *domain.User) bool {
					return u.Email == "alice@example.com"
				})).Return(nil)
			},
			check: func(t *testing.T, res *service.RegisterResult) {
				assert.Equal(t, "alice@example.com", res.User.Email)
			},
		},
		{
			name:    "corner case — empty email returns ErrInvalidEmail",
			cmd:     service.RegisterCmd{Email: "", Password: "Password1!"},
			wantErr: domain.ErrInvalidEmail,
		},
		{
			name:    "corner case — invalid email format returns ErrInvalidEmail",
			cmd:     service.RegisterCmd{Email: "not-an-email", Password: "Password1!"},
			wantErr: domain.ErrInvalidEmail,
		},
		{
			name:    "corner case — email missing TLD returns ErrInvalidEmail",
			cmd:     service.RegisterCmd{Email: "user@domain", Password: "Password1!"},
			wantErr: domain.ErrInvalidEmail,
		},
		{
			name:    "corner case — password shorter than 8 chars returns ErrPasswordTooShort",
			cmd:     service.RegisterCmd{Email: "user@example.com", Password: "short"},
			wantErr: domain.ErrPasswordTooShort,
		},
		{
			name:    "corner case — password exactly 7 chars returns ErrPasswordTooShort",
			cmd:     service.RegisterCmd{Email: "user@example.com", Password: "1234567"},
			wantErr: domain.ErrPasswordTooShort,
		},
		{
			name: "corner case — duplicate email returns ErrEmailAlreadyTaken",
			cmd:  service.RegisterCmd{Email: "existing@example.com", Password: "Password1!"},
			setupMock: func(r *mockUserRepo) {
				r.On("Create", mock.Anything, mock.Anything).Return(domain.ErrEmailAlreadyTaken)
			},
			wantErr: domain.ErrEmailAlreadyTaken,
		},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			repo := &mockUserRepo{}
			if tt.setupMock != nil {
				tt.setupMock(repo)
			}

			res, err := newSvc(repo).Register(context.Background(), tt.cmd)

			if tt.wantErr != nil {
				require.Error(t, err)
				assert.ErrorIs(t, err, tt.wantErr)
				assert.Nil(t, res)
				return
			}

			require.NoError(t, err)
			require.NotNil(t, res)
			if tt.check != nil {
				tt.check(t, res)
			}
			repo.AssertExpectations(t)
		})
	}
}

// ---- Login tests ----

func TestLogin(t *testing.T) {
	tests := []struct {
		name      string
		cmd       service.LoginCmd
		setupMock func(*mockUserRepo)
		wantErr   error
		check     func(*testing.T, *service.TokenResult)
	}{
		{
			name: "happy path — correct credentials return token",
			cmd:  service.LoginCmd{Email: "user@example.com", Password: "Correct!1"},
			setupMock: func(r *mockUserRepo) {
				r.On("GetByEmail", mock.Anything, "user@example.com").Return(&domain.User{
					ID:           uuid.New(),
					Email:        "user@example.com",
					PasswordHash: hashedPassword(t, "Correct!1"),
				}, nil)
			},
			check: func(t *testing.T, res *service.TokenResult) {
				assert.NotEmpty(t, res.Token)
				assert.True(t, res.ExpiresAt.After(time.Now()))
			},
		},
		{
			name: "corner case — wrong password returns ErrInvalidCredentials",
			cmd:  service.LoginCmd{Email: "user@example.com", Password: "WrongPass"},
			setupMock: func(r *mockUserRepo) {
				r.On("GetByEmail", mock.Anything, "user@example.com").Return(&domain.User{
					ID:           uuid.New(),
					Email:        "user@example.com",
					PasswordHash: hashedPassword(t, "Correct!1"),
				}, nil)
			},
			wantErr: domain.ErrInvalidCredentials,
		},
		{
			name: "corner case — non-existent user returns ErrInvalidCredentials (no enumeration)",
			cmd:  service.LoginCmd{Email: "ghost@example.com", Password: "Any"},
			setupMock: func(r *mockUserRepo) {
				r.On("GetByEmail", mock.Anything, "ghost@example.com").Return(nil, domain.ErrUserNotFound)
			},
			// Must return same error as wrong password — no user enumeration.
			wantErr: domain.ErrInvalidCredentials,
		},
		{
			name: "corner case — email is normalized before lookup",
			cmd:  service.LoginCmd{Email: "  USER@EXAMPLE.COM  ", Password: "Correct!1"},
			setupMock: func(r *mockUserRepo) {
				r.On("GetByEmail", mock.Anything, "user@example.com").Return(&domain.User{
					ID:           uuid.New(),
					Email:        "user@example.com",
					PasswordHash: hashedPassword(t, "Correct!1"),
				}, nil)
			},
			check: func(t *testing.T, res *service.TokenResult) {
				assert.NotEmpty(t, res.Token)
			},
		},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			repo := &mockUserRepo{}
			if tt.setupMock != nil {
				tt.setupMock(repo)
			}

			res, err := newSvc(repo).Login(context.Background(), tt.cmd)

			if tt.wantErr != nil {
				require.Error(t, err)
				assert.ErrorIs(t, err, tt.wantErr)
				assert.Nil(t, res)
				return
			}

			require.NoError(t, err)
			require.NotNil(t, res)
			if tt.check != nil {
				tt.check(t, res)
			}
			repo.AssertExpectations(t)
		})
	}
}
