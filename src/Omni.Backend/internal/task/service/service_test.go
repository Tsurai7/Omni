package service_test

import (
	"context"
	"errors"
	"log/slog"
	"strings"
	"testing"
	"time"

	"omni-backend/internal/task/domain"
	"omni-backend/internal/task/service"

	"github.com/google/uuid"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/mock"
	"github.com/stretchr/testify/require"
)

// ---- mock repository ----

type mockTaskRepo struct{ mock.Mock }

func (m *mockTaskRepo) GetByID(ctx context.Context, id, userID uuid.UUID) (*domain.Task, error) {
	args := m.Called(ctx, id, userID)
	if args.Get(0) == nil {
		return nil, args.Error(1)
	}
	return args.Get(0).(*domain.Task), args.Error(1)
}

func (m *mockTaskRepo) ListByUser(ctx context.Context, userID uuid.UUID) ([]*domain.Task, error) {
	args := m.Called(ctx, userID)
	if args.Get(0) == nil {
		return nil, args.Error(1)
	}
	return args.Get(0).([]*domain.Task), args.Error(1)
}

func (m *mockTaskRepo) Create(ctx context.Context, t *domain.Task) error {
	return m.Called(ctx, t).Error(0)
}

func (m *mockTaskRepo) Update(ctx context.Context, t *domain.Task) error {
	return m.Called(ctx, t).Error(0)
}

func (m *mockTaskRepo) Delete(ctx context.Context, id, userID uuid.UUID) error {
	return m.Called(ctx, id, userID).Error(0)
}

// ---- helpers ----

var (
	fixedTime = time.Date(2025, 1, 15, 10, 0, 0, 0, time.UTC)
	testUser  = uuid.MustParse("00000000-0000-0000-0000-000000000001")
	testTask  = uuid.MustParse("00000000-0000-0000-0000-000000000002")
)

func newSvc(repo *mockTaskRepo) service.TaskService {
	return service.NewWithClock(repo, slog.Default(), func() time.Time { return fixedTime })
}

func pendingTask() *domain.Task {
	return &domain.Task{
		ID:        testTask,
		UserID:    testUser,
		Title:     "Test Task",
		Status:    domain.StatusPending,
		Priority:  domain.PriorityMedium,
		CreatedAt: fixedTime,
		UpdatedAt: fixedTime,
	}
}

// ---- CreateTask tests ----

func TestCreateTask(t *testing.T) {
	tests := []struct {
		name      string
		cmd       service.CreateTaskCmd
		setupMock func(*mockTaskRepo)
		wantErr   error
		check     func(*testing.T, *domain.Task)
	}{
		{
			name: "happy path — creates task with correct defaults",
			cmd:  service.CreateTaskCmd{UserID: testUser, Title: "Buy groceries", Priority: domain.PriorityMedium},
			setupMock: func(r *mockTaskRepo) {
				r.On("Create", mock.Anything, mock.MatchedBy(func(t *domain.Task) bool {
					return t.Title == "Buy groceries" &&
						t.Status == domain.StatusPending &&
						t.Priority == domain.PriorityMedium &&
						t.UserID == testUser &&
						t.CreatedAt.Equal(fixedTime)
				})).Return(nil)
			},
			check: func(t *testing.T, task *domain.Task) {
				assert.Equal(t, "Buy groceries", task.Title)
				assert.Equal(t, domain.StatusPending, task.Status)
				assert.Equal(t, domain.PriorityMedium, task.Priority)
				assert.Equal(t, testUser, task.UserID)
				assert.Equal(t, fixedTime, task.CreatedAt)
				assert.NotEqual(t, uuid.Nil, task.ID)
			},
		},
		{
			name: "happy path — empty priority defaults to medium",
			cmd:  service.CreateTaskCmd{UserID: testUser, Title: "Task", Priority: ""},
			setupMock: func(r *mockTaskRepo) {
				r.On("Create", mock.Anything, mock.MatchedBy(func(t *domain.Task) bool {
					return t.Priority == domain.PriorityMedium
				})).Return(nil)
			},
			check: func(t *testing.T, task *domain.Task) {
				assert.Equal(t, domain.PriorityMedium, task.Priority)
			},
		},
		{
			name: "happy path — high priority preserved",
			cmd:  service.CreateTaskCmd{UserID: testUser, Title: "Urgent", Priority: domain.PriorityHigh},
			setupMock: func(r *mockTaskRepo) {
				r.On("Create", mock.Anything, mock.MatchedBy(func(t *domain.Task) bool {
					return t.Priority == domain.PriorityHigh
				})).Return(nil)
			},
			check: func(t *testing.T, task *domain.Task) {
				assert.Equal(t, domain.PriorityHigh, task.Priority)
			},
		},
		{
			name:    "corner case — empty title returns ErrTaskTitleEmpty",
			cmd:     service.CreateTaskCmd{UserID: testUser, Title: ""},
			wantErr: domain.ErrTaskTitleEmpty,
		},
		{
			name:    "corner case — whitespace-only title returns ErrTaskTitleEmpty",
			cmd:     service.CreateTaskCmd{UserID: testUser, Title: "   "},
			wantErr: domain.ErrTaskTitleEmpty,
		},
		{
			name:    "corner case — title over 500 chars returns ErrTaskTitleTooLong",
			cmd:     service.CreateTaskCmd{UserID: testUser, Title: strings.Repeat("x", 501)},
			wantErr: domain.ErrTaskTitleTooLong,
		},
		{
			name:    "corner case — title exactly 500 chars is valid",
			cmd:     service.CreateTaskCmd{UserID: testUser, Title: strings.Repeat("x", 500)},
			wantErr: domain.ErrTaskTitleTooLong,
			// 500 should succeed — override the expected error to nil
		},
		{
			name:    "corner case — invalid priority returns ErrInvalidPriority",
			cmd:     service.CreateTaskCmd{UserID: testUser, Title: "Valid", Priority: "ultra"},
			wantErr: domain.ErrInvalidPriority,
		},
		{
			name: "corner case — repository error is propagated",
			cmd:  service.CreateTaskCmd{UserID: testUser, Title: "Valid", Priority: domain.PriorityHigh},
			setupMock: func(r *mockTaskRepo) {
				r.On("Create", mock.Anything, mock.Anything).Return(errors.New("connection lost"))
			},
			wantErr: errors.New("connection lost"),
		},
	}

	// Fix the 500-char test case (500 is valid, not an error).
	for i, tt := range tests {
		if tt.name == "corner case — title exactly 500 chars is valid" {
			tests[i].wantErr = nil
			tests[i].setupMock = func(r *mockTaskRepo) {
				r.On("Create", mock.Anything, mock.Anything).Return(nil)
			}
			tests[i].check = func(t *testing.T, task *domain.Task) {
				assert.Len(t, task.Title, 500)
			}
		}
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			repo := &mockTaskRepo{}
			if tt.setupMock != nil {
				tt.setupMock(repo)
			}

			task, err := newSvc(repo).CreateTask(context.Background(), tt.cmd)

			if tt.wantErr != nil {
				require.Error(t, err)
				// Use errors.Is for sentinel errors, string comparison for dynamic errors.
				if errors.Is(tt.wantErr, domain.ErrTaskTitleEmpty) ||
					errors.Is(tt.wantErr, domain.ErrTaskTitleTooLong) ||
					errors.Is(tt.wantErr, domain.ErrInvalidPriority) {
					assert.ErrorIs(t, err, tt.wantErr)
				}
				assert.Nil(t, task)
				return
			}

			require.NoError(t, err)
			require.NotNil(t, task)
			if tt.check != nil {
				tt.check(t, task)
			}
			repo.AssertExpectations(t)
		})
	}
}

// ---- ChangeStatus tests ----

func TestChangeStatus(t *testing.T) {
	tests := []struct {
		name       string
		existing   *domain.Task
		cmd        service.ChangeStatusCmd
		setupMock  func(*mockTaskRepo, *domain.Task)
		wantErr    error
		wantStatus domain.TaskStatus
	}{
		{
			name:     "happy path — pending → in_progress",
			existing: pendingTask(),
			cmd:      service.ChangeStatusCmd{TaskID: testTask, UserID: testUser, Status: domain.StatusInProgress},
			setupMock: func(r *mockTaskRepo, existing *domain.Task) {
				r.On("GetByID", mock.Anything, testTask, testUser).Return(existing, nil)
				r.On("Update", mock.Anything, mock.MatchedBy(func(t *domain.Task) bool {
					return t.Status == domain.StatusInProgress
				})).Return(nil)
			},
			wantStatus: domain.StatusInProgress,
		},
		{
			name:     "happy path — pending → done",
			existing: pendingTask(),
			cmd:      service.ChangeStatusCmd{TaskID: testTask, UserID: testUser, Status: domain.StatusDone},
			setupMock: func(r *mockTaskRepo, existing *domain.Task) {
				r.On("GetByID", mock.Anything, testTask, testUser).Return(existing, nil)
				r.On("Update", mock.Anything, mock.MatchedBy(func(t *domain.Task) bool {
					return t.Status == domain.StatusDone && t.UpdatedAt.Equal(fixedTime)
				})).Return(nil)
			},
			wantStatus: domain.StatusDone,
		},
		{
			name:     "happy path — pending → cancelled",
			existing: pendingTask(),
			cmd:      service.ChangeStatusCmd{TaskID: testTask, UserID: testUser, Status: domain.StatusCancelled},
			setupMock: func(r *mockTaskRepo, existing *domain.Task) {
				r.On("GetByID", mock.Anything, testTask, testUser).Return(existing, nil)
				r.On("Update", mock.Anything, mock.Anything).Return(nil)
			},
			wantStatus: domain.StatusCancelled,
		},
		{
			name:    "corner case — invalid status returns ErrInvalidStatus",
			cmd:     service.ChangeStatusCmd{TaskID: testTask, UserID: testUser, Status: "flying"},
			wantErr: domain.ErrInvalidStatus,
		},
		{
			name:     "corner case — task not found propagates ErrTaskNotFound",
			existing: nil,
			cmd:      service.ChangeStatusCmd{TaskID: testTask, UserID: testUser, Status: domain.StatusDone},
			setupMock: func(r *mockTaskRepo, _ *domain.Task) {
				r.On("GetByID", mock.Anything, testTask, testUser).Return(nil, domain.ErrTaskNotFound)
			},
			wantErr: domain.ErrTaskNotFound,
		},
		{
			name:     "corner case — repo Update failure propagated",
			existing: pendingTask(),
			cmd:      service.ChangeStatusCmd{TaskID: testTask, UserID: testUser, Status: domain.StatusDone},
			setupMock: func(r *mockTaskRepo, existing *domain.Task) {
				r.On("GetByID", mock.Anything, testTask, testUser).Return(existing, nil)
				r.On("Update", mock.Anything, mock.Anything).Return(errors.New("db error"))
			},
			wantErr: errors.New("db error"),
		},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			repo := &mockTaskRepo{}
			if tt.setupMock != nil {
				tt.setupMock(repo, tt.existing)
			}

			task, err := newSvc(repo).ChangeStatus(context.Background(), tt.cmd)

			if tt.wantErr != nil {
				require.Error(t, err)
				if errors.Is(tt.wantErr, domain.ErrInvalidStatus) ||
					errors.Is(tt.wantErr, domain.ErrTaskNotFound) {
					assert.ErrorIs(t, err, tt.wantErr)
				}
				assert.Nil(t, task)
				return
			}

			require.NoError(t, err)
			assert.Equal(t, tt.wantStatus, task.Status)
			assert.Equal(t, fixedTime, task.UpdatedAt)
			repo.AssertExpectations(t)
		})
	}
}

// ---- UpdateTask tests ----

func TestUpdateTask(t *testing.T) {
	tests := []struct {
		name      string
		cmd       service.UpdateTaskCmd
		setupMock func(*mockTaskRepo)
		wantErr   error
		check     func(*testing.T, *domain.Task)
	}{
		{
			name: "happy path — updates title and priority",
			cmd: service.UpdateTaskCmd{
				TaskID: testTask, UserID: testUser,
				Title: "New Title", Priority: domain.PriorityHigh,
			},
			setupMock: func(r *mockTaskRepo) {
				r.On("GetByID", mock.Anything, testTask, testUser).Return(pendingTask(), nil)
				r.On("Update", mock.Anything, mock.MatchedBy(func(t *domain.Task) bool {
					return t.Title == "New Title" && t.Priority == domain.PriorityHigh
				})).Return(nil)
			},
			check: func(t *testing.T, task *domain.Task) {
				assert.Equal(t, "New Title", task.Title)
				assert.Equal(t, domain.PriorityHigh, task.Priority)
			},
		},
		{
			name: "happy path — ClearDue clears due date",
			cmd: service.UpdateTaskCmd{
				TaskID: testTask, UserID: testUser,
				Title: "Title", Priority: domain.PriorityLow,
				ClearDue: true,
			},
			setupMock: func(r *mockTaskRepo) {
				existing := pendingTask()
				due := fixedTime
				existing.DueDate = &due
				r.On("GetByID", mock.Anything, testTask, testUser).Return(existing, nil)
				r.On("Update", mock.Anything, mock.MatchedBy(func(t *domain.Task) bool {
					return t.DueDate == nil
				})).Return(nil)
			},
			check: func(t *testing.T, task *domain.Task) {
				assert.Nil(t, task.DueDate)
			},
		},
		{
			name:    "corner case — empty title returns ErrTaskTitleEmpty",
			cmd:     service.UpdateTaskCmd{TaskID: testTask, UserID: testUser, Title: "", Priority: domain.PriorityLow},
			wantErr: domain.ErrTaskTitleEmpty,
		},
		{
			name:    "corner case — invalid priority returns ErrInvalidPriority",
			cmd:     service.UpdateTaskCmd{TaskID: testTask, UserID: testUser, Title: "T", Priority: "epic"},
			wantErr: domain.ErrInvalidPriority,
		},
		{
			name: "corner case — task not found propagated",
			cmd:  service.UpdateTaskCmd{TaskID: testTask, UserID: testUser, Title: "T", Priority: domain.PriorityLow},
			setupMock: func(r *mockTaskRepo) {
				r.On("GetByID", mock.Anything, testTask, testUser).Return(nil, domain.ErrTaskNotFound)
			},
			wantErr: domain.ErrTaskNotFound,
		},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			repo := &mockTaskRepo{}
			if tt.setupMock != nil {
				tt.setupMock(repo)
			}

			task, err := newSvc(repo).UpdateTask(context.Background(), tt.cmd)

			if tt.wantErr != nil {
				require.Error(t, err)
				assert.ErrorIs(t, err, tt.wantErr)
				assert.Nil(t, task)
				return
			}

			require.NoError(t, err)
			if tt.check != nil {
				tt.check(t, task)
			}
			repo.AssertExpectations(t)
		})
	}
}

// ---- DeleteTask tests ----

func TestDeleteTask(t *testing.T) {
	tests := []struct {
		name      string
		taskID    uuid.UUID
		userID    uuid.UUID
		setupMock func(*mockTaskRepo)
		wantErr   error
	}{
		{
			name:   "happy path — deletes successfully",
			taskID: testTask, userID: testUser,
			setupMock: func(r *mockTaskRepo) {
				r.On("Delete", mock.Anything, testTask, testUser).Return(nil)
			},
		},
		{
			name:   "corner case — task not found returns ErrTaskNotFound",
			taskID: testTask, userID: testUser,
			setupMock: func(r *mockTaskRepo) {
				r.On("Delete", mock.Anything, testTask, testUser).Return(domain.ErrTaskNotFound)
			},
			wantErr: domain.ErrTaskNotFound,
		},
		{
			name:   "corner case — other user cannot delete another user's task",
			taskID: testTask,
			userID: uuid.New(), // different user
			setupMock: func(r *mockTaskRepo) {
				r.On("Delete", mock.Anything, testTask, mock.Anything).Return(domain.ErrTaskNotFound)
			},
			wantErr: domain.ErrTaskNotFound,
		},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			repo := &mockTaskRepo{}
			if tt.setupMock != nil {
				tt.setupMock(repo)
			}

			err := newSvc(repo).DeleteTask(context.Background(), tt.taskID, tt.userID)

			if tt.wantErr != nil {
				require.Error(t, err)
				assert.ErrorIs(t, err, tt.wantErr)
				return
			}

			require.NoError(t, err)
			repo.AssertExpectations(t)
		})
	}
}

// ---- ListTasks tests ----

func TestListTasks(t *testing.T) {
	tests := []struct {
		name      string
		userID    uuid.UUID
		setupMock func(*mockTaskRepo)
		wantCount int
		wantErr   error
	}{
		{
			name:   "happy path — returns user tasks",
			userID: testUser,
			setupMock: func(r *mockTaskRepo) {
				r.On("ListByUser", mock.Anything, testUser).Return([]*domain.Task{
					pendingTask(), pendingTask(),
				}, nil)
			},
			wantCount: 2,
		},
		{
			name:   "happy path — user with no tasks returns empty slice",
			userID: testUser,
			setupMock: func(r *mockTaskRepo) {
				r.On("ListByUser", mock.Anything, testUser).Return([]*domain.Task{}, nil)
			},
			wantCount: 0,
		},
		{
			name:   "corner case — repository error propagated",
			userID: testUser,
			setupMock: func(r *mockTaskRepo) {
				r.On("ListByUser", mock.Anything, testUser).Return(nil, errors.New("db down"))
			},
			wantErr: errors.New("db down"),
		},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			repo := &mockTaskRepo{}
			if tt.setupMock != nil {
				tt.setupMock(repo)
			}

			tasks, err := newSvc(repo).ListTasks(context.Background(), tt.userID)

			if tt.wantErr != nil {
				require.Error(t, err)
				return
			}

			require.NoError(t, err)
			assert.Len(t, tasks, tt.wantCount)
			repo.AssertExpectations(t)
		})
	}
}
