package handler_test

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"log/slog"
	"net/http"
	"net/http/httptest"
	"testing"
	"time"

	"omni-backend/internal/auth"
	"omni-backend/internal/profile/domain"
	"omni-backend/internal/profile/handler"
	"omni-backend/internal/profile/service"

	"github.com/gin-gonic/gin"
	"github.com/google/uuid"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/mock"
	"github.com/stretchr/testify/require"
)

// ---- mock service ----

type mockAuthSvc struct{ mock.Mock }

func (m *mockAuthSvc) Register(ctx context.Context, cmd service.RegisterCmd) (*service.RegisterResult, error) {
	args := m.Called(ctx, cmd)
	if args.Get(0) == nil {
		return nil, args.Error(1)
	}
	return args.Get(0).(*service.RegisterResult), args.Error(1)
}

func (m *mockAuthSvc) Login(ctx context.Context, cmd service.LoginCmd) (*service.TokenResult, error) {
	args := m.Called(ctx, cmd)
	if args.Get(0) == nil {
		return nil, args.Error(1)
	}
	return args.Get(0).(*service.TokenResult), args.Error(1)
}

func (m *mockAuthSvc) GetUser(ctx context.Context, userID uuid.UUID) (*domain.User, error) {
	args := m.Called(ctx, userID)
	if args.Get(0) == nil {
		return nil, args.Error(1)
	}
	return args.Get(0).(*domain.User), args.Error(1)
}

// ---- helpers ----

func setupRouter(svc service.AuthService) *gin.Engine {
	gin.SetMode(gin.TestMode)
	r := gin.New()
	h := handler.New(svc, slog.Default())
	public := r.Group("/api/auth")
	protected := r.Group("/api/auth")
	protected.Use(func(c *gin.Context) {
		c.Set("claims", &auth.Claims{UserID: "11111111-1111-1111-1111-111111111111", Email: "test@test.com"})
		c.Next()
	})
	h.RegisterRoutes(public, protected)
	return r
}

func doRequest(t *testing.T, r *gin.Engine, method, path string, body any) *httptest.ResponseRecorder {
	t.Helper()
	var buf bytes.Buffer
	if body != nil {
		require.NoError(t, json.NewEncoder(&buf).Encode(body))
	}
	req := httptest.NewRequest(method, path, &buf)
	if body != nil {
		req.Header.Set("Content-Type", "application/json")
	}
	w := httptest.NewRecorder()
	r.ServeHTTP(w, req)
	return w
}

func decodeBody(t *testing.T, w *httptest.ResponseRecorder) map[string]any {
	t.Helper()
	var m map[string]any
	require.NoError(t, json.Unmarshal(w.Body.Bytes(), &m))
	return m
}

func successRegisterResult() *service.RegisterResult {
	return &service.RegisterResult{
		User: &domain.User{
			ID:    uuid.New(),
			Email: "alice@example.com",
		},
		Token:     "tok.en.here",
		ExpiresAt: time.Now().Add(24 * time.Hour),
	}
}

// ---- Register tests ----

func TestRegister(t *testing.T) {
	tests := []struct {
		name       string
		body       any
		setup      func(*mockAuthSvc)
		wantStatus int
		check      func(*testing.T, *httptest.ResponseRecorder)
	}{
		{
			name: "happy path — 201 with user and token",
			body: map[string]any{"email": "alice@example.com", "password": "Secure123!"},
			setup: func(s *mockAuthSvc) {
				s.On("Register", mock.Anything, service.RegisterCmd{
					Email: "alice@example.com", Password: "Secure123!",
				}).Return(successRegisterResult(), nil)
			},
			wantStatus: http.StatusCreated,
			check: func(t *testing.T, w *httptest.ResponseRecorder) {
				body := decodeBody(t, w)
				assert.Equal(t, "alice@example.com", body["email"])
				assert.NotEmpty(t, body["token"])
				assert.NotEmpty(t, body["expires_at"])
			},
		},
		{
			name:       "corner case — missing email field → 400",
			body:       map[string]any{"password": "Secure123!"},
			wantStatus: http.StatusBadRequest,
		},
		{
			name:       "corner case — missing password field → 400",
			body:       map[string]any{"email": "alice@example.com"},
			wantStatus: http.StatusBadRequest,
		},
		{
			name:       "corner case — malformed JSON → 400",
			body:       "not-json",
			wantStatus: http.StatusBadRequest,
		},
		{
			name: "corner case — invalid email → 400",
			body: map[string]any{"email": "notanemail", "password": "Pass123!"},
			setup: func(s *mockAuthSvc) {
				s.On("Register", mock.Anything, mock.Anything).Return(nil, domain.ErrInvalidEmail)
			},
			wantStatus: http.StatusBadRequest,
		},
		{
			name: "corner case — password too short → 400",
			body: map[string]any{"email": "a@b.com", "password": "short"},
			setup: func(s *mockAuthSvc) {
				s.On("Register", mock.Anything, mock.Anything).Return(nil, domain.ErrPasswordTooShort)
			},
			wantStatus: http.StatusBadRequest,
		},
		{
			name: "corner case — duplicate email → 409",
			body: map[string]any{"email": "taken@example.com", "password": "Pass123!"},
			setup: func(s *mockAuthSvc) {
				s.On("Register", mock.Anything, mock.Anything).Return(nil, domain.ErrEmailAlreadyTaken)
			},
			wantStatus: http.StatusConflict,
		},
		{
			name: "corner case — unexpected error → 500 generic message",
			body: map[string]any{"email": "a@b.com", "password": "Pass123!"},
			setup: func(s *mockAuthSvc) {
				s.On("Register", mock.Anything, mock.Anything).Return(nil, errors.New("db down"))
			},
			wantStatus: http.StatusInternalServerError,
			check: func(t *testing.T, w *httptest.ResponseRecorder) {
				body := decodeBody(t, w)
				assert.Equal(t, "internal server error", body["error"])
			},
		},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			svc := &mockAuthSvc{}
			if tt.setup != nil {
				tt.setup(svc)
			}
			r := setupRouter(svc)
			w := doRequest(t, r, http.MethodPost, "/api/auth/register", tt.body)

			assert.Equal(t, tt.wantStatus, w.Code)
			if tt.check != nil {
				tt.check(t, w)
			}
			svc.AssertExpectations(t)
		})
	}
}

// ---- Login tests ----

func TestLogin(t *testing.T) {
	tests := []struct {
		name       string
		body       any
		setup      func(*mockAuthSvc)
		wantStatus int
		check      func(*testing.T, *httptest.ResponseRecorder)
	}{
		{
			name: "happy path — 200 with token",
			body: map[string]any{"email": "alice@example.com", "password": "Correct!1"},
			setup: func(s *mockAuthSvc) {
				s.On("Login", mock.Anything, service.LoginCmd{
					Email: "alice@example.com", Password: "Correct!1",
				}).Return(&service.TokenResult{
					Token: "tok.en", ExpiresAt: time.Now().Add(24 * time.Hour),
				}, nil)
			},
			wantStatus: http.StatusOK,
			check: func(t *testing.T, w *httptest.ResponseRecorder) {
				body := decodeBody(t, w)
				assert.Equal(t, "tok.en", body["token"])
			},
		},
		{
			name: "corner case — wrong password → 401",
			body: map[string]any{"email": "alice@example.com", "password": "Wrong"},
			setup: func(s *mockAuthSvc) {
				s.On("Login", mock.Anything, mock.Anything).Return(nil, domain.ErrInvalidCredentials)
			},
			wantStatus: http.StatusUnauthorized,
		},
		{
			name: "corner case — unknown user → 401 (same as wrong password, no enumeration)",
			body: map[string]any{"email": "ghost@example.com", "password": "Any"},
			setup: func(s *mockAuthSvc) {
				s.On("Login", mock.Anything, mock.Anything).Return(nil, domain.ErrInvalidCredentials)
			},
			wantStatus: http.StatusUnauthorized,
		},
		{
			name:       "corner case — missing fields → 400",
			body:       map[string]any{},
			wantStatus: http.StatusBadRequest,
		},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			svc := &mockAuthSvc{}
			if tt.setup != nil {
				tt.setup(svc)
			}
			r := setupRouter(svc)
			w := doRequest(t, r, http.MethodPost, "/api/auth/login", tt.body)

			assert.Equal(t, tt.wantStatus, w.Code)
			if tt.check != nil {
				tt.check(t, w)
			}
			svc.AssertExpectations(t)
		})
	}
}

// ---- Me tests ----

func TestMe(t *testing.T) {
	svc := &mockAuthSvc{}
	r := setupRouter(svc)
	w := doRequest(t, r, http.MethodGet, "/api/auth/me", nil)

	assert.Equal(t, http.StatusOK, w.Code)
	body := decodeBody(t, w)
	assert.Equal(t, "11111111-1111-1111-1111-111111111111", body["id"])
	assert.Equal(t, "test@test.com", body["email"])
}
