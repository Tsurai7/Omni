package task

// CreateTaskRequest is the JSON body for POST /tasks.
type CreateTaskRequest struct {
	Title  string `json:"title"`
	Status string `json:"status"` // optional, defaults to "pending"
}

// UpdateStatusRequest is the JSON body for PATCH /tasks/:id/status.
type UpdateStatusRequest struct {
	Status string `json:"status"` // pending | done | cancelled
}

// TaskResponse is a single task in API responses (snake_case for client compatibility).
type TaskResponse struct {
	ID        string `json:"id"`
	UserID    string `json:"user_id"`
	Title     string `json:"title"`
	Status    string `json:"status"`
	CreatedAt string `json:"created_at"`
	UpdatedAt string `json:"updated_at"`
}

// ListResponse is the response for GET /tasks.
type ListResponse struct {
	Tasks []TaskResponse `json:"tasks"`
}
