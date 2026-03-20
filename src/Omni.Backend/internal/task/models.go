package task

// CreateTaskRequest is the JSON body for POST /tasks.
type CreateTaskRequest struct {
	Title        string  `json:"title"`
	Status       string  `json:"status"`        // optional, defaults to "pending"
	Priority     string  `json:"priority"`      // optional, defaults to "medium"
	DueDate      *string `json:"due_date"`      // optional RFC3339 timestamp
	ScheduledFor *string `json:"scheduled_for"` // optional RFC3339 timestamp
}

// UpdateStatusRequest is the JSON body for PATCH /tasks/:id/status.
type UpdateStatusRequest struct {
	Status string `json:"status"` // pending | in_progress | done | cancelled
}

// UpdateTaskRequest is the JSON body for PUT /tasks/:id.
type UpdateTaskRequest struct {
	Title        string  `json:"title"`
	Priority     string  `json:"priority"`      // low | medium | high
	DueDate      *string `json:"due_date"`      // optional RFC3339 timestamp; null clears the field
	ScheduledFor *string `json:"scheduled_for"` // optional RFC3339 timestamp; null clears the field
}

// TaskResponse is a single task in API responses (snake_case for client compatibility).
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

// ListResponse is the response for GET /tasks.
type ListResponse struct {
	Tasks []TaskResponse `json:"tasks"`
}
