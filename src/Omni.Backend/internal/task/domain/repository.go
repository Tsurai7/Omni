package domain

import (
	"context"

	"github.com/google/uuid"
)

// TaskRepository is the persistence port. Implementations live in
// internal/task/repository/. Tests use a mock generated from this interface.
type TaskRepository interface {
	// GetByID fetches a task owned by userID. Returns ErrTaskNotFound when
	// the record does not exist or belongs to a different user.
	GetByID(ctx context.Context, id, userID uuid.UUID) (*Task, error)

	// ListByUser returns all tasks for the given user, newest first.
	ListByUser(ctx context.Context, userID uuid.UUID) ([]*Task, error)

	// Create persists a new task. The Task.ID must be pre-populated.
	Create(ctx context.Context, task *Task) error

	// Update persists mutations to an existing task (all writable fields).
	Update(ctx context.Context, task *Task) error

	// Delete removes the task. Returns ErrTaskNotFound when no row is affected.
	Delete(ctx context.Context, id, userID uuid.UUID) error
}
