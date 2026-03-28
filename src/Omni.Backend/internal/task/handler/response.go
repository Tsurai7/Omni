package handler

import (
	"time"

	"omni-backend/internal/task/domain"
)

// TaskResponse is the JSON representation of a task returned to clients.
// Date fields are formatted as RFC3339 strings for backward compatibility.
type TaskResponse struct {
	ID            string  `json:"id"`
	UserID        string  `json:"user_id"`
	Title         string  `json:"title"`
	Status        string  `json:"status"`
	Priority      string  `json:"priority"`
	DueDate       *string `json:"due_date,omitempty"`
	ScheduledFor  *string `json:"scheduled_for,omitempty"`
	GoogleEventID *string `json:"google_event_id,omitempty"`
	CreatedAt     string  `json:"created_at"`
	UpdatedAt     string  `json:"updated_at"`
}

// ListResponse wraps a task slice for GET /api/tasks.
type ListResponse struct {
	Tasks []TaskResponse `json:"tasks"`
}

func toTaskResponse(t *domain.Task) TaskResponse {
	resp := TaskResponse{
		ID:            t.ID.String(),
		UserID:        t.UserID.String(),
		Title:         t.Title,
		Status:        string(t.Status),
		Priority:      string(t.Priority),
		GoogleEventID: t.GoogleEventID,
		CreatedAt:     t.CreatedAt.UTC().Format(time.RFC3339),
		UpdatedAt:     t.UpdatedAt.UTC().Format(time.RFC3339),
	}
	if t.DueDate != nil {
		s := t.DueDate.UTC().Format(time.RFC3339)
		resp.DueDate = &s
	}
	if t.ScheduledFor != nil {
		s := t.ScheduledFor.UTC().Format(time.RFC3339)
		resp.ScheduledFor = &s
	}
	return resp
}

type errBody struct {
	Error string `json:"error"`
}

func errResp(msg string) errBody { return errBody{Error: msg} }
