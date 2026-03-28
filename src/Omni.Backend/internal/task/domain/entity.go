// Package domain contains pure business types for the task domain.
// No HTTP, SQL, or serialization concerns live here.
package domain

import (
	"time"

	"github.com/google/uuid"
)

// TaskStatus represents the lifecycle state of a task.
type TaskStatus string

// TaskPriority represents how urgent a task is.
type TaskPriority string

const (
	StatusPending    TaskStatus = "pending"
	StatusInProgress TaskStatus = "in_progress"
	StatusDone       TaskStatus = "done"
	StatusCancelled  TaskStatus = "cancelled"
)

const (
	PriorityLow    TaskPriority = "low"
	PriorityMedium TaskPriority = "medium"
	PriorityHigh   TaskPriority = "high"
)

// IsValid reports whether the status is one of the known values.
func (s TaskStatus) IsValid() bool {
	switch s {
	case StatusPending, StatusInProgress, StatusDone, StatusCancelled:
		return true
	}
	return false
}

// IsValid reports whether the priority is one of the known values.
func (p TaskPriority) IsValid() bool {
	switch p {
	case PriorityLow, PriorityMedium, PriorityHigh:
		return true
	}
	return false
}

// Task is the core aggregate for a user's task.
type Task struct {
	ID            uuid.UUID
	UserID        uuid.UUID
	Title         string
	Status        TaskStatus
	Priority      TaskPriority
	DueDate       *time.Time
	ScheduledFor  *time.Time
	GoogleEventID *string
	CreatedAt     time.Time
	UpdatedAt     time.Time
}
