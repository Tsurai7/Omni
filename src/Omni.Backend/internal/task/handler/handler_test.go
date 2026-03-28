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
	"omni-backend/internal/task/domain"
	"omni-backend/internal/task/handler"
	"omni-backend/internal/task/service"

	"github.com/gin-gonic/gin"
	"github.com/google/uuid"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/mock"
	"github.com/stretchr/testify/require"
)

// ---- mock service ----

type mockTaskSvc struct{ mock.Mock }

func (m *mockTaskSvc) GetTask(ctx context.Context, taskID, userID uuid.UUID) (*domain.Task, error) {
	args := m.Called(ctx, taskID, userID)
	if args.Get(0) == nil {
		return nil, args.Error(1)
	}
	return args.Get(0).(*domain.Task), args.Error(1)
}

func (m *mockTaskSvc) ListTasks(ctx context.Context, userID uuid.UUID) ([]*domain.Task, error) {
	args := m.Called(ctx, userID)
	if args.Get(0) == nil {
		return nil, args.Error(1)
	}
	return args.Get(0).([]*domain.Task), args.Error(1)
}

func (m *mockTaskSvc) CreateTask(ctx context.Context, cmd service.CreateTaskCmd) (*domain.Task, error) {
	args := m.Called(ctx, cmd)
	if args.Get(0) == nil {
		return nil, args.Error(1)
	}
	return args.Get(0).(*domain.Task), args.Error(1)
}

func (m *mockTaskSvc) UpdateTask(ctx context.Context, cmd service.UpdateTaskCmd) (*domain.Task, error) {
	args := m.Called(ctx, cmd)
	if args.Get(0) == nil {
		return nil, args.Error(1)
	}
	return args.Get(0).(*domain.Task), args.Error(1)
}

func (m *mockTaskSvc) ChangeStatus(ctx context.Context, cmd service.ChangeStatusCmd) (*domain.Task, error) {
	args := m.Called(ctx, cmd)
	if args.Get(0) == nil {
		return nil, args.Error(1)
	}
	return args.Get(0).(*domain.Task), args.Error(1)
}

func (m *mockTaskSvc) DeleteTask(ctx context.Context, taskID, userID uuid.UUID) error {
	return m.Called(ctx, taskID, userID).Error(0)
}

// ---- test helpers ----

var (
	testUserID = uuid.MustParse("11111111-1111-1111-1111-111111111111")
	testTaskID = uuid.MustParse("22222222-2222-2222-2222-222222222222")
)

func setupRouter(svc service.TaskService) *gin.Engine {
	gin.SetMode(gin.TestMode)
	r := gin.New()
	r.Use(func(c *gin.Context) {
		// Simulate the JWT middleware injecting claims.
		c.Set("claims", &auth.Claims{UserID: testUserID.String(), Email: "test@test.com"})
		c.Next()
	})
	h := handler.New(svc, slog.Default())
	h.RegisterRoutes(r.Group("/api/tasks"))
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

func sampleTask() *domain.Task {
	return &domain.Task{
		ID:       testTaskID,
		UserID:   testUserID,
		Title:    "Sample Task",
		Status:   domain.StatusPending,
		Priority: domain.PriorityMedium,
	}
}

// ---- List tests ----

func TestList(t *testing.T) {
	tests := []struct {
		name       string
		setup      func(*mockTaskSvc)
		wantStatus int
		check      func(*testing.T, *httptest.ResponseRecorder)
	}{
		{
			name: "happy path — returns task list",
			setup: func(s *mockTaskSvc) {
				s.On("ListTasks", mock.Anything, testUserID).Return([]*domain.Task{sampleTask()}, nil)
			},
			wantStatus: http.StatusOK,
			check: func(t *testing.T, w *httptest.ResponseRecorder) {
				body := decodeBody(t, w)
				tasks := body["tasks"].([]any)
				assert.Len(t, tasks, 1)
			},
		},
		{
			name: "happy path — empty list returns empty array not null",
			setup: func(s *mockTaskSvc) {
				s.On("ListTasks", mock.Anything, testUserID).Return([]*domain.Task{}, nil)
			},
			wantStatus: http.StatusOK,
			check: func(t *testing.T, w *httptest.ResponseRecorder) {
				body := decodeBody(t, w)
				tasks := body["tasks"].([]any)
				assert.Empty(t, tasks)
			},
		},
		{
			name: "corner case — service error returns 500",
			setup: func(s *mockTaskSvc) {
				s.On("ListTasks", mock.Anything, testUserID).Return(nil, errors.New("db error"))
			},
			wantStatus: http.StatusInternalServerError,
		},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			svc := &mockTaskSvc{}
			if tt.setup != nil {
				tt.setup(svc)
			}
			r := setupRouter(svc)
			w := doRequest(t, r, http.MethodGet, "/api/tasks", nil)

			assert.Equal(t, tt.wantStatus, w.Code)
			if tt.check != nil {
				tt.check(t, w)
			}
			svc.AssertExpectations(t)
		})
	}
}

// ---- Create tests ----

func TestCreate(t *testing.T) {
	tests := []struct {
		name       string
		body       any
		setup      func(*mockTaskSvc)
		wantStatus int
		check      func(*testing.T, *httptest.ResponseRecorder)
	}{
		{
			name: "happy path — 201 with task body",
			body: map[string]any{"title": "Buy milk", "priority": "medium"},
			setup: func(s *mockTaskSvc) {
				s.On("CreateTask", mock.Anything, mock.MatchedBy(func(cmd service.CreateTaskCmd) bool {
					return cmd.Title == "Buy milk" && cmd.UserID == testUserID
				})).Return(sampleTask(), nil)
			},
			wantStatus: http.StatusCreated,
			check: func(t *testing.T, w *httptest.ResponseRecorder) {
				body := decodeBody(t, w)
				assert.Equal(t, "Sample Task", body["title"])
			},
		},
		{
			name:       "corner case — missing title binding fails → 400",
			body:       map[string]any{"priority": "medium"},
			wantStatus: http.StatusBadRequest,
		},
		{
			name:       "corner case — malformed JSON → 400",
			body:       "not-json",
			wantStatus: http.StatusBadRequest,
		},
		{
			name: "corner case — service returns ErrTaskTitleEmpty → 400",
			body: map[string]any{"title": "  "},
			setup: func(s *mockTaskSvc) {
				s.On("CreateTask", mock.Anything, mock.Anything).Return(nil, domain.ErrTaskTitleEmpty)
			},
			wantStatus: http.StatusBadRequest,
		},
		{
			name:       "corner case — invalid due_date format → 400",
			body:       map[string]any{"title": "T", "due_date": "not-a-date"},
			wantStatus: http.StatusBadRequest,
		},
		{
			name: "corner case — service returns ErrInvalidPriority → 400",
			body: map[string]any{"title": "T", "priority": "epic"},
			setup: func(s *mockTaskSvc) {
				s.On("CreateTask", mock.Anything, mock.Anything).Return(nil, domain.ErrInvalidPriority)
			},
			wantStatus: http.StatusBadRequest,
		},
		{
			name: "corner case — unexpected service error → 500 with generic message",
			body: map[string]any{"title": "T"},
			setup: func(s *mockTaskSvc) {
				s.On("CreateTask", mock.Anything, mock.Anything).Return(nil, errors.New("db down"))
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
			svc := &mockTaskSvc{}
			if tt.setup != nil {
				tt.setup(svc)
			}
			r := setupRouter(svc)
			w := doRequest(t, r, http.MethodPost, "/api/tasks", tt.body)

			assert.Equal(t, tt.wantStatus, w.Code)
			if tt.check != nil {
				tt.check(t, w)
			}
			svc.AssertExpectations(t)
		})
	}
}

// ---- UpdateStatus tests ----

func TestUpdateStatus(t *testing.T) {
	tests := []struct {
		name       string
		taskID     string
		body       any
		setup      func(*mockTaskSvc)
		wantStatus int
	}{
		{
			name:   "happy path — status updated",
			taskID: testTaskID.String(),
			body:   map[string]any{"status": "done"},
			setup: func(s *mockTaskSvc) {
				s.On("ChangeStatus", mock.Anything, service.ChangeStatusCmd{
					TaskID: testTaskID, UserID: testUserID, Status: domain.StatusDone,
				}).Return(sampleTask(), nil)
			},
			wantStatus: http.StatusOK,
		},
		{
			name:       "corner case — invalid UUID path param → 400",
			taskID:     "not-a-uuid",
			body:       map[string]any{"status": "done"},
			wantStatus: http.StatusBadRequest,
		},
		{
			name:   "corner case — task not found → 404",
			taskID: testTaskID.String(),
			body:   map[string]any{"status": "done"},
			setup: func(s *mockTaskSvc) {
				s.On("ChangeStatus", mock.Anything, mock.Anything).Return(nil, domain.ErrTaskNotFound)
			},
			wantStatus: http.StatusNotFound,
		},
		{
			name:   "corner case — invalid status → 400",
			taskID: testTaskID.String(),
			body:   map[string]any{"status": "flying"},
			setup: func(s *mockTaskSvc) {
				s.On("ChangeStatus", mock.Anything, mock.Anything).Return(nil, domain.ErrInvalidStatus)
			},
			wantStatus: http.StatusBadRequest,
		},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			svc := &mockTaskSvc{}
			if tt.setup != nil {
				tt.setup(svc)
			}
			r := setupRouter(svc)
			w := doRequest(t, r, http.MethodPatch, "/api/tasks/"+tt.taskID+"/status", tt.body)

			assert.Equal(t, tt.wantStatus, w.Code)
			svc.AssertExpectations(t)
		})
	}
}

// ---- Delete tests ----

func TestDelete(t *testing.T) {
	tests := []struct {
		name       string
		taskID     string
		setup      func(*mockTaskSvc)
		wantStatus int
	}{
		{
			name:   "happy path — 204 no content",
			taskID: testTaskID.String(),
			setup: func(s *mockTaskSvc) {
				s.On("DeleteTask", mock.Anything, testTaskID, testUserID).Return(nil)
			},
			wantStatus: http.StatusNoContent,
		},
		{
			name:       "corner case — invalid UUID → 400",
			taskID:     "not-uuid",
			wantStatus: http.StatusBadRequest,
		},
		{
			name:   "corner case — not found → 404",
			taskID: testTaskID.String(),
			setup: func(s *mockTaskSvc) {
				s.On("DeleteTask", mock.Anything, testTaskID, testUserID).Return(domain.ErrTaskNotFound)
			},
			wantStatus: http.StatusNotFound,
		},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			svc := &mockTaskSvc{}
			if tt.setup != nil {
				tt.setup(svc)
			}
			r := setupRouter(svc)
			w := doRequest(t, r, http.MethodDelete, "/api/tasks/"+tt.taskID, nil)

			assert.Equal(t, tt.wantStatus, w.Code)
			svc.AssertExpectations(t)
		})
	}
}
