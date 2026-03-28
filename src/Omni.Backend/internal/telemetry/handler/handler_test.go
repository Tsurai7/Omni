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

	"omni-backend/internal/auth"
	tdomain "omni-backend/internal/telemetry/domain"
	"omni-backend/internal/telemetry/handler"
	"omni-backend/internal/telemetry/service"

	"github.com/gin-gonic/gin"
	"github.com/google/uuid"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/mock"
	"github.com/stretchr/testify/require"
)

// ---- mock service ----

type mockTelemetrySvc struct{ mock.Mock }

func (m *mockTelemetrySvc) SyncUsage(ctx context.Context, cmd service.SyncUsageCmd) error {
	return m.Called(ctx, cmd).Error(0)
}

func (m *mockTelemetrySvc) ListUsage(ctx context.Context, q tdomain.UsageQuery) ([]*tdomain.UsageAggregate, error) {
	args := m.Called(ctx, q)
	if args.Get(0) == nil {
		return nil, args.Error(1)
	}
	return args.Get(0).([]*tdomain.UsageAggregate), args.Error(1)
}

func (m *mockTelemetrySvc) SyncSessions(ctx context.Context, cmd service.SyncSessionsCmd) error {
	return m.Called(ctx, cmd).Error(0)
}

func (m *mockTelemetrySvc) ListSessions(ctx context.Context, q tdomain.SessionQuery) ([]*tdomain.Session, error) {
	args := m.Called(ctx, q)
	if args.Get(0) == nil {
		return nil, args.Error(1)
	}
	return args.Get(0).([]*tdomain.Session), args.Error(1)
}

func (m *mockTelemetrySvc) ListNotifications(ctx context.Context, userID uuid.UUID, unreadOnly bool) ([]*tdomain.Notification, error) {
	args := m.Called(ctx, userID, unreadOnly)
	if args.Get(0) == nil {
		return nil, args.Error(1)
	}
	return args.Get(0).([]*tdomain.Notification), args.Error(1)
}

func (m *mockTelemetrySvc) MarkNotificationRead(ctx context.Context, notifID, userID uuid.UUID) error {
	return m.Called(ctx, notifID, userID).Error(0)
}

// ---- helpers ----

var testUserID = uuid.MustParse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")

func setupRouter(svc service.TelemetryService) *gin.Engine {
	gin.SetMode(gin.TestMode)
	r := gin.New()
	r.Use(func(c *gin.Context) {
		c.Set("claims", &auth.Claims{UserID: testUserID.String(), Email: "test@test.com"})
		c.Next()
	})
	h := handler.New(svc, slog.Default())
	api := r.Group("/api")
	h.RegisterRoutes(
		api.Group("/usage"),
		api.Group("/sessions"),
		api.Group("/productivity"),
	)
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

// ---- SyncUsage tests ----

func TestSyncUsage(t *testing.T) {
	tests := []struct {
		name       string
		body       any
		setup      func(*mockTelemetrySvc)
		wantStatus int
	}{
		{
			name: "happy path — entries synced",
			body: map[string]any{"entries": []any{
				map[string]any{"app_name": "VSCode", "category": "Coding", "duration_seconds": 3600},
			}},
			setup: func(s *mockTelemetrySvc) {
				s.On("SyncUsage", mock.Anything, mock.MatchedBy(func(cmd service.SyncUsageCmd) bool {
					return cmd.UserID == testUserID && len(cmd.Entries) == 1
				})).Return(nil)
			},
			wantStatus: http.StatusOK,
		},
		{
			name: "happy path — empty entries returns 200",
			body: map[string]any{"entries": []any{}},
			setup: func(s *mockTelemetrySvc) {
				s.On("SyncUsage", mock.Anything, mock.Anything).Return(nil)
			},
			wantStatus: http.StatusOK,
		},
		{
			name:       "corner case — malformed JSON → 400",
			body:       "not-json",
			wantStatus: http.StatusBadRequest,
		},
		{
			name: "corner case — service error → 500",
			body: map[string]any{"entries": []any{
				map[string]any{"app_name": "App", "duration_seconds": 100},
			}},
			setup: func(s *mockTelemetrySvc) {
				s.On("SyncUsage", mock.Anything, mock.Anything).Return(errors.New("db error"))
			},
			wantStatus: http.StatusInternalServerError,
		},
		{
			name: "corner case — invalid started_at in session → 400",
			body: map[string]any{"entries": []any{
				map[string]any{"app_name": "App", "duration_seconds": 100},
			}},
			setup: func(s *mockTelemetrySvc) {
				s.On("SyncUsage", mock.Anything, mock.Anything).Return(tdomain.ErrInvalidStartedAt)
			},
			wantStatus: http.StatusBadRequest,
		},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			svc := &mockTelemetrySvc{}
			if tt.setup != nil {
				tt.setup(svc)
			}
			r := setupRouter(svc)
			w := doRequest(t, r, http.MethodPost, "/api/usage/sync", tt.body)
			assert.Equal(t, tt.wantStatus, w.Code)
			svc.AssertExpectations(t)
		})
	}
}

// ---- SyncSessions tests ----

func TestSyncSessions(t *testing.T) {
	tests := []struct {
		name       string
		body       any
		setup      func(*mockTelemetrySvc)
		wantStatus int
	}{
		{
			name: "happy path — sessions synced",
			body: map[string]any{"entries": []any{
				map[string]any{
					"name": "Deep Work", "activity_type": "focus",
					"started_at": "2025-01-15T10:00:00Z", "duration_seconds": 3600,
				},
			}},
			setup: func(s *mockTelemetrySvc) {
				s.On("SyncSessions", mock.Anything, mock.MatchedBy(func(cmd service.SyncSessionsCmd) bool {
					return cmd.UserID == testUserID && len(cmd.Entries) == 1
				})).Return(nil)
			},
			wantStatus: http.StatusOK,
		},
		{
			name:       "corner case — malformed JSON → 400",
			body:       "not-json",
			wantStatus: http.StatusBadRequest,
		},
		{
			name: "corner case — invalid started_at → 400",
			body: map[string]any{"entries": []any{
				map[string]any{"name": "Session", "started_at": "bad-date", "duration_seconds": 100},
			}},
			setup: func(s *mockTelemetrySvc) {
				s.On("SyncSessions", mock.Anything, mock.Anything).Return(tdomain.ErrInvalidStartedAt)
			},
			wantStatus: http.StatusBadRequest,
		},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			svc := &mockTelemetrySvc{}
			if tt.setup != nil {
				tt.setup(svc)
			}
			r := setupRouter(svc)
			w := doRequest(t, r, http.MethodPost, "/api/sessions/sync", tt.body)
			assert.Equal(t, tt.wantStatus, w.Code)
			svc.AssertExpectations(t)
		})
	}
}

// ---- MarkNotificationRead tests ----

func TestMarkNotificationRead(t *testing.T) {
	validID := uuid.New().String()

	tests := []struct {
		name       string
		id         string
		setup      func(*mockTelemetrySvc)
		wantStatus int
	}{
		{
			name: "happy path — 204 no content",
			id:   validID,
			setup: func(s *mockTelemetrySvc) {
				s.On("MarkNotificationRead", mock.Anything, mock.Anything, testUserID).Return(nil)
			},
			wantStatus: http.StatusNoContent,
		},
		{
			name:       "corner case — invalid UUID → 400",
			id:         "not-uuid",
			wantStatus: http.StatusBadRequest,
		},
		{
			name: "corner case — not found → 404",
			id:   validID,
			setup: func(s *mockTelemetrySvc) {
				s.On("MarkNotificationRead", mock.Anything, mock.Anything, testUserID).Return(tdomain.ErrNotificationNotFound)
			},
			wantStatus: http.StatusNotFound,
		},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			svc := &mockTelemetrySvc{}
			if tt.setup != nil {
				tt.setup(svc)
			}
			r := setupRouter(svc)
			w := doRequest(t, r, http.MethodPatch, "/api/productivity/notifications/"+tt.id+"/read", nil)
			assert.Equal(t, tt.wantStatus, w.Code)
			svc.AssertExpectations(t)
		})
	}
}
