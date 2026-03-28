// Package service contains the task business logic.
// It depends only on the domain package — no HTTP, SQL, or Kafka.
package service

import (
	"context"
	"log/slog"
	"strings"
	"time"

	"omni-backend/internal/task/domain"

	"github.com/google/uuid"
)

// TaskService is the application-level use-case interface for tasks.
type TaskService interface {
	GetTask(ctx context.Context, taskID, userID uuid.UUID) (*domain.Task, error)
	ListTasks(ctx context.Context, userID uuid.UUID) ([]*domain.Task, error)
	CreateTask(ctx context.Context, cmd CreateTaskCmd) (*domain.Task, error)
	UpdateTask(ctx context.Context, cmd UpdateTaskCmd) (*domain.Task, error)
	ChangeStatus(ctx context.Context, cmd ChangeStatusCmd) (*domain.Task, error)
	DeleteTask(ctx context.Context, taskID, userID uuid.UUID) error
}

// CreateTaskCmd is the input value object for creating a task.
type CreateTaskCmd struct {
	UserID       uuid.UUID
	Title        string
	Priority     domain.TaskPriority // empty → defaults to medium
	DueDate      *time.Time
	ScheduledFor *time.Time
}

// UpdateTaskCmd is the input value object for updating a task's mutable fields.
type UpdateTaskCmd struct {
	TaskID       uuid.UUID
	UserID       uuid.UUID
	Title        string
	Priority     domain.TaskPriority
	DueDate      *time.Time // nil clears the field
	ScheduledFor *time.Time // nil clears the field
	ClearDue     bool       // explicit nil signal from caller
	ClearSched   bool       // explicit nil signal from caller
}

// ChangeStatusCmd is the input value object for changing a task's status.
type ChangeStatusCmd struct {
	TaskID uuid.UUID
	UserID uuid.UUID
	Status domain.TaskStatus
}

type taskService struct {
	repo   domain.TaskRepository
	logger *slog.Logger
	clock  func() time.Time
}

// New returns a TaskService wired to the given repository.
func New(repo domain.TaskRepository, logger *slog.Logger) TaskService {
	return &taskService{repo: repo, logger: logger, clock: time.Now}
}

// NewWithClock returns a TaskService with an injectable clock for testing.
func NewWithClock(repo domain.TaskRepository, logger *slog.Logger, clock func() time.Time) TaskService {
	return &taskService{repo: repo, logger: logger, clock: clock}
}

func (s *taskService) GetTask(ctx context.Context, taskID, userID uuid.UUID) (*domain.Task, error) {
	return s.repo.GetByID(ctx, taskID, userID)
}

func (s *taskService) ListTasks(ctx context.Context, userID uuid.UUID) ([]*domain.Task, error) {
	return s.repo.ListByUser(ctx, userID)
}

func (s *taskService) CreateTask(ctx context.Context, cmd CreateTaskCmd) (*domain.Task, error) {
	title := strings.TrimSpace(cmd.Title)
	if title == "" {
		return nil, domain.ErrTaskTitleEmpty
	}
	if len(title) > 500 {
		return nil, domain.ErrTaskTitleTooLong
	}

	priority := cmd.Priority
	if priority == "" {
		priority = domain.PriorityMedium
	}
	if !priority.IsValid() {
		return nil, domain.ErrInvalidPriority
	}

	now := s.clock()
	task := &domain.Task{
		ID:           uuid.New(),
		UserID:       cmd.UserID,
		Title:        title,
		Status:       domain.StatusPending,
		Priority:     priority,
		DueDate:      cmd.DueDate,
		ScheduledFor: cmd.ScheduledFor,
		CreatedAt:    now,
		UpdatedAt:    now,
	}

	if err := s.repo.Create(ctx, task); err != nil {
		s.logger.ErrorContext(ctx, "failed to create task", "user_id", cmd.UserID, "error", err)
		return nil, err
	}

	s.logger.InfoContext(ctx, "task created", "task_id", task.ID, "user_id", cmd.UserID)
	return task, nil
}

func (s *taskService) UpdateTask(ctx context.Context, cmd UpdateTaskCmd) (*domain.Task, error) {
	title := strings.TrimSpace(cmd.Title)
	if title == "" {
		return nil, domain.ErrTaskTitleEmpty
	}
	if len(title) > 500 {
		return nil, domain.ErrTaskTitleTooLong
	}

	priority := cmd.Priority
	if priority == "" {
		priority = domain.PriorityMedium
	}
	if !priority.IsValid() {
		return nil, domain.ErrInvalidPriority
	}

	task, err := s.repo.GetByID(ctx, cmd.TaskID, cmd.UserID)
	if err != nil {
		return nil, err
	}

	task.Title = title
	task.Priority = priority
	task.UpdatedAt = s.clock()

	// Explicit nil signals from handler (JSON null = clear the field).
	if cmd.ClearDue {
		task.DueDate = nil
	} else if cmd.DueDate != nil {
		task.DueDate = cmd.DueDate
	}
	if cmd.ClearSched {
		task.ScheduledFor = nil
	} else if cmd.ScheduledFor != nil {
		task.ScheduledFor = cmd.ScheduledFor
	}

	if err := s.repo.Update(ctx, task); err != nil {
		s.logger.ErrorContext(ctx, "failed to update task", "task_id", cmd.TaskID, "error", err)
		return nil, err
	}

	s.logger.InfoContext(ctx, "task updated", "task_id", task.ID, "user_id", cmd.UserID)
	return task, nil
}

func (s *taskService) ChangeStatus(ctx context.Context, cmd ChangeStatusCmd) (*domain.Task, error) {
	if !cmd.Status.IsValid() {
		return nil, domain.ErrInvalidStatus
	}

	task, err := s.repo.GetByID(ctx, cmd.TaskID, cmd.UserID)
	if err != nil {
		return nil, err
	}

	task.Status = cmd.Status
	task.UpdatedAt = s.clock()

	if err := s.repo.Update(ctx, task); err != nil {
		s.logger.ErrorContext(ctx, "failed to change task status", "task_id", cmd.TaskID, "error", err)
		return nil, err
	}

	s.logger.InfoContext(ctx, "task status changed",
		"task_id", task.ID, "user_id", cmd.UserID, "status", cmd.Status)
	return task, nil
}

func (s *taskService) DeleteTask(ctx context.Context, taskID, userID uuid.UUID) error {
	if err := s.repo.Delete(ctx, taskID, userID); err != nil {
		if err != domain.ErrTaskNotFound {
			s.logger.ErrorContext(ctx, "failed to delete task", "task_id", taskID, "error", err)
		}
		return err
	}
	s.logger.InfoContext(ctx, "task deleted", "task_id", taskID, "user_id", userID)
	return nil
}
