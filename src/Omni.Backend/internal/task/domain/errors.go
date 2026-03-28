package domain

import "errors"

// Sentinel errors used by the task domain.
// Service and handler layers match on these with errors.Is.
var (
	ErrTaskNotFound        = errors.New("task not found")
	ErrTaskTitleEmpty      = errors.New("task title cannot be empty")
	ErrTaskTitleTooLong    = errors.New("task title exceeds 500 characters")
	ErrInvalidStatus       = errors.New("invalid task status")
	ErrInvalidPriority     = errors.New("invalid task priority")
	ErrInvalidDueDate      = errors.New("due_date must be a valid RFC3339 timestamp")
	ErrInvalidScheduledFor = errors.New("scheduled_for must be a valid RFC3339 timestamp")
)
