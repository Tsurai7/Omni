package handler

import (
	"time"

	"omni-backend/internal/task/domain"
	"omni-backend/internal/task/service"

	"github.com/google/uuid"
)

// CreateTaskRequest is the JSON body for POST /api/tasks.
type CreateTaskRequest struct {
	Title        string              `json:"title"         binding:"required"`
	Priority     domain.TaskPriority `json:"priority"`
	DueDate      *string             `json:"due_date"`
	ScheduledFor *string             `json:"scheduled_for"`
}

// UpdateTaskRequest is the JSON body for PUT /api/tasks/:id.
// A JSON null for due_date or scheduled_for explicitly clears the field.
type UpdateTaskRequest struct {
	Title        string              `json:"title"         binding:"required"`
	Priority     domain.TaskPriority `json:"priority"`
	DueDate      *string             `json:"due_date"`
	ScheduledFor *string             `json:"scheduled_for"`
}

// ChangeStatusRequest is the JSON body for PATCH /api/tasks/:id/status.
type ChangeStatusRequest struct {
	Status domain.TaskStatus `json:"status" binding:"required"`
}

func (r *CreateTaskRequest) toCommand(userID uuid.UUID) (service.CreateTaskCmd, error) {
	cmd := service.CreateTaskCmd{
		UserID:   userID,
		Title:    r.Title,
		Priority: r.Priority,
	}
	var err error
	if r.DueDate != nil {
		t, parseErr := time.Parse(time.RFC3339, *r.DueDate)
		if parseErr != nil {
			return cmd, domain.ErrInvalidDueDate
		}
		cmd.DueDate = &t
		err = nil
	}
	if r.ScheduledFor != nil {
		t, parseErr := time.Parse(time.RFC3339, *r.ScheduledFor)
		if parseErr != nil {
			return cmd, domain.ErrInvalidScheduledFor
		}
		cmd.ScheduledFor = &t
		err = nil
	}
	_ = err
	return cmd, nil
}

func (r *UpdateTaskRequest) toCommand(taskID, userID uuid.UUID) (service.UpdateTaskCmd, error) {
	cmd := service.UpdateTaskCmd{
		TaskID:   taskID,
		UserID:   userID,
		Title:    r.Title,
		Priority: r.Priority,
		// nil pointer in the JSON field means "clear the column"
		ClearDue:   r.DueDate == nil,
		ClearSched: r.ScheduledFor == nil,
	}
	if r.DueDate != nil {
		t, err := time.Parse(time.RFC3339, *r.DueDate)
		if err != nil {
			return cmd, domain.ErrInvalidDueDate
		}
		cmd.DueDate = &t
	}
	if r.ScheduledFor != nil {
		t, err := time.Parse(time.RFC3339, *r.ScheduledFor)
		if err != nil {
			return cmd, domain.ErrInvalidScheduledFor
		}
		cmd.ScheduledFor = &t
	}
	return cmd, nil
}
