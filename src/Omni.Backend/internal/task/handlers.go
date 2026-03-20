package task

import (
	"log/slog"
	"net/http"
	"strings"

	"omni-backend/internal/middleware"

	"github.com/gin-gonic/gin"
	"github.com/google/uuid"
	"github.com/jackc/pgx/v5/pgxpool"
)

type Handler struct {
	Pool   *pgxpool.Pool
	logger *slog.Logger
}

func NewHandler(pool *pgxpool.Pool, logger *slog.Logger) *Handler {
	return &Handler{Pool: pool, logger: logger}
}

var allowedStatuses = map[string]bool{"pending": true, "in_progress": true, "done": true, "cancelled": true}
var allowedPriorities = map[string]bool{"low": true, "medium": true, "high": true}

// List returns all tasks for the authenticated user.
// List godoc
// @Summary      List tasks
// @Tags         tasks
// @Security     BearerAuth
// @Produce      json
// @Success      200  {object}  ListResponse
// @Failure      401  {object}  map[string]string
// @Router       /tasks [get]
func (h *Handler) List(c *gin.Context) {
	claims := middleware.GetClaims(c)
	if claims == nil {
		c.JSON(http.StatusUnauthorized, gin.H{"error": "unauthorized"})
		return
	}
	userID, err := uuid.Parse(claims.UserID)
	if err != nil {
		c.JSON(http.StatusBadRequest, gin.H{"error": "invalid user"})
		return
	}

	ctx := c.Request.Context()
	rows, err := h.Pool.Query(ctx,
		`SELECT id::text, user_id::text, title, status, priority,
		        to_char(due_date AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"'),
		        to_char(scheduled_for AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"'),
		        google_event_id,
		        created_at::text, updated_at::text
		 FROM tasks WHERE user_id = $1 ORDER BY created_at DESC`,
		userID)
	if err != nil {
		h.logger.Error("failed to list tasks", "user_id", userID, "error", err)
		c.JSON(http.StatusInternalServerError, gin.H{"error": "failed to list tasks"})
		return
	}
	defer rows.Close()

	var tasks []TaskResponse
	for rows.Next() {
		var t TaskResponse
		if err := rows.Scan(&t.ID, &t.UserID, &t.Title, &t.Status, &t.Priority, &t.DueDate, &t.ScheduledFor, &t.GoogleEventID, &t.CreatedAt, &t.UpdatedAt); err != nil {
			h.logger.Error("failed to scan task row", "user_id", userID, "error", err)
			c.JSON(http.StatusInternalServerError, gin.H{"error": "failed to list tasks"})
			return
		}
		tasks = append(tasks, t)
	}
	if tasks == nil {
		tasks = []TaskResponse{}
	}
	h.logger.Debug("tasks listed", "user_id", userID, "count", len(tasks))
	c.JSON(http.StatusOK, ListResponse{Tasks: tasks})
}

// Create creates a new task for the authenticated user.
// Create godoc
// @Summary      Create a task
// @Tags         tasks
// @Security     BearerAuth
// @Accept       json
// @Produce      json
// @Param        body  body      CreateTaskRequest  true  "Task title and optional status/priority"
// @Success      201   {object}  TaskResponse
// @Failure      400   {object}  map[string]string
// @Failure      401   {object}  map[string]string
// @Router       /tasks [post]
func (h *Handler) Create(c *gin.Context) {
	claims := middleware.GetClaims(c)
	if claims == nil {
		c.JSON(http.StatusUnauthorized, gin.H{"error": "unauthorized"})
		return
	}
	userID, err := uuid.Parse(claims.UserID)
	if err != nil {
		c.JSON(http.StatusBadRequest, gin.H{"error": "invalid user"})
		return
	}

	var req CreateTaskRequest
	if err := c.ShouldBindJSON(&req); err != nil {
		c.JSON(http.StatusBadRequest, gin.H{"error": "invalid request body"})
		return
	}
	title := strings.TrimSpace(req.Title)
	if title == "" {
		c.JSON(http.StatusBadRequest, gin.H{"error": "title is required"})
		return
	}
	status := req.Status
	if status == "" || !allowedStatuses[status] {
		status = "pending"
	}
	priority := req.Priority
	if priority == "" || !allowedPriorities[priority] {
		priority = "medium"
	}

	ctx := c.Request.Context()
	var id string
	var createdAt, updatedAt string
	var dueDate, scheduledFor *string
	err = h.Pool.QueryRow(ctx,
		`INSERT INTO tasks (user_id, title, status, priority, due_date, scheduled_for)
		 VALUES ($1, $2, $3, $4,
		         CASE WHEN $5::text IS NOT NULL THEN $5::timestamptz ELSE NULL END,
		         CASE WHEN $6::text IS NOT NULL THEN $6::timestamptz ELSE NULL END)
		 RETURNING id::text, created_at::text, updated_at::text,
		           to_char(due_date AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"'),
		           to_char(scheduled_for AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"')`,
		userID, title, status, priority, req.DueDate, req.ScheduledFor).
		Scan(&id, &createdAt, &updatedAt, &dueDate, &scheduledFor)
	if err != nil {
		h.logger.Error("failed to create task", "user_id", userID, "error", err)
		c.JSON(http.StatusInternalServerError, gin.H{"error": "failed to create task"})
		return
	}
	h.logger.Info("task created", "task_id", id, "user_id", userID, "status", status, "priority", priority)
	c.JSON(http.StatusCreated, TaskResponse{
		ID:           id,
		UserID:       userID.String(),
		Title:        title,
		Status:       status,
		Priority:     priority,
		DueDate:      dueDate,
		ScheduledFor: scheduledFor,
		CreatedAt:    createdAt,
		UpdatedAt:    updatedAt,
	})
}

// UpdateStatus updates a task's status.
// UpdateStatus godoc
// @Summary      Update task status
// @Tags         tasks
// @Security     BearerAuth
// @Accept       json
// @Produce      json
// @Param        id    path     string  true  "Task ID"
// @Param        body  body     UpdateStatusRequest  true  "New status"
// @Success      200   {object}  TaskResponse
// @Failure      400   {object}  map[string]string
// @Failure      401   {object}  map[string]string
// @Failure      404   {object}  map[string]string
// @Router       /tasks/{id}/status [patch]
func (h *Handler) UpdateStatus(c *gin.Context) {
	claims := middleware.GetClaims(c)
	if claims == nil {
		c.JSON(http.StatusUnauthorized, gin.H{"error": "unauthorized"})
		return
	}
	userID, err := uuid.Parse(claims.UserID)
	if err != nil {
		c.JSON(http.StatusBadRequest, gin.H{"error": "invalid user"})
		return
	}
	taskID, err := uuid.Parse(c.Param("id"))
	if err != nil {
		c.JSON(http.StatusBadRequest, gin.H{"error": "invalid task id"})
		return
	}

	var req UpdateStatusRequest
	if err := c.ShouldBindJSON(&req); err != nil {
		c.JSON(http.StatusBadRequest, gin.H{"error": "invalid request body"})
		return
	}
	status := strings.TrimSpace(req.Status)
	if !allowedStatuses[status] {
		c.JSON(http.StatusBadRequest, gin.H{"error": "status must be pending, in_progress, done, or cancelled"})
		return
	}

	ctx := c.Request.Context()
	var id, title, priority, createdAt, updatedAt string
	var uid uuid.UUID
	var dueDate, scheduledFor, googleEventID *string
	err = h.Pool.QueryRow(ctx,
		`UPDATE tasks SET status = $1, updated_at = NOW() WHERE id = $2 AND user_id = $3
		 RETURNING id::text, user_id, title, status, priority,
		           to_char(due_date AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"'),
		           to_char(scheduled_for AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"'),
		           google_event_id,
		           created_at::text, updated_at::text`,
		status, taskID, userID).Scan(&id, &uid, &title, &status, &priority, &dueDate, &scheduledFor, &googleEventID, &createdAt, &updatedAt)
	if err != nil {
		if err.Error() == "no rows in result set" {
			c.JSON(http.StatusNotFound, gin.H{"error": "task not found"})
			return
		}
		h.logger.Error("failed to update task status", "task_id", taskID, "user_id", userID, "error", err)
		c.JSON(http.StatusInternalServerError, gin.H{"error": "failed to update task"})
		return
	}
	h.logger.Info("task status updated", "task_id", id, "user_id", userID, "status", status)
	c.JSON(http.StatusOK, TaskResponse{
		ID:            id,
		UserID:        uid.String(),
		Title:         title,
		Status:        status,
		Priority:      priority,
		DueDate:       dueDate,
		ScheduledFor:  scheduledFor,
		GoogleEventID: googleEventID,
		CreatedAt:     createdAt,
		UpdatedAt:     updatedAt,
	})
}

// Update updates a task's title and priority.
// Update godoc
// @Summary      Update task
// @Tags         tasks
// @Security     BearerAuth
// @Accept       json
// @Produce      json
// @Param        id    path     string  true  "Task ID"
// @Param        body  body     UpdateTaskRequest  true  "New title and priority"
// @Success      200   {object}  TaskResponse
// @Failure      400   {object}  map[string]string
// @Failure      401   {object}  map[string]string
// @Failure      404   {object}  map[string]string
// @Router       /tasks/{id} [put]
func (h *Handler) Update(c *gin.Context) {
	claims := middleware.GetClaims(c)
	if claims == nil {
		c.JSON(http.StatusUnauthorized, gin.H{"error": "unauthorized"})
		return
	}
	userID, err := uuid.Parse(claims.UserID)
	if err != nil {
		c.JSON(http.StatusBadRequest, gin.H{"error": "invalid user"})
		return
	}
	taskID, err := uuid.Parse(c.Param("id"))
	if err != nil {
		c.JSON(http.StatusBadRequest, gin.H{"error": "invalid task id"})
		return
	}

	var req UpdateTaskRequest
	if err := c.ShouldBindJSON(&req); err != nil {
		c.JSON(http.StatusBadRequest, gin.H{"error": "invalid request body"})
		return
	}
	title := strings.TrimSpace(req.Title)
	if title == "" {
		c.JSON(http.StatusBadRequest, gin.H{"error": "title is required"})
		return
	}
	priority := req.Priority
	if priority == "" || !allowedPriorities[priority] {
		priority = "medium"
	}

	ctx := c.Request.Context()
	var id, status, createdAt, updatedAt string
	var uid uuid.UUID
	var dueDate, scheduledFor *string
	err = h.Pool.QueryRow(ctx,
		`UPDATE tasks
		 SET title = $1, priority = $2, updated_at = NOW(),
		     due_date     = CASE WHEN $5::boolean THEN NULL
		                        WHEN $3::text IS NOT NULL THEN $3::timestamptz
		                        ELSE due_date END,
		     scheduled_for = CASE WHEN $6::boolean THEN NULL
		                         WHEN $4::text IS NOT NULL THEN $4::timestamptz
		                         ELSE scheduled_for END
		 WHERE id = $7 AND user_id = $8
		 RETURNING id::text, user_id, title, status, priority,
		           to_char(due_date AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"'),
		           to_char(scheduled_for AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"'),
		           created_at::text, updated_at::text`,
		title, priority,
		req.DueDate, req.ScheduledFor,
		req.DueDate == nil, req.ScheduledFor == nil,
		taskID, userID).
		Scan(&id, &uid, &title, &status, &priority, &dueDate, &scheduledFor, &createdAt, &updatedAt)
	if err != nil {
		if err.Error() == "no rows in result set" {
			c.JSON(http.StatusNotFound, gin.H{"error": "task not found"})
			return
		}
		h.logger.Error("failed to update task", "task_id", taskID, "user_id", userID, "error", err)
		c.JSON(http.StatusInternalServerError, gin.H{"error": "failed to update task"})
		return
	}
	h.logger.Info("task updated", "task_id", id, "user_id", userID, "priority", priority)
	c.JSON(http.StatusOK, TaskResponse{
		ID:           id,
		UserID:       uid.String(),
		Title:        title,
		Status:       status,
		Priority:     priority,
		DueDate:      dueDate,
		ScheduledFor: scheduledFor,
		CreatedAt:    createdAt,
		UpdatedAt:    updatedAt,
	})
}

// Delete deletes a task.
// Delete godoc
// @Summary      Delete a task
// @Tags         tasks
// @Security     BearerAuth
// @Param        id   path      string  true  "Task ID"
// @Success      204  "No Content"
// @Failure      400  {object}  map[string]string
// @Failure      401  {object}  map[string]string
// @Failure      404  {object}  map[string]string
// @Router       /tasks/{id} [delete]
func (h *Handler) Delete(c *gin.Context) {
	claims := middleware.GetClaims(c)
	if claims == nil {
		c.JSON(http.StatusUnauthorized, gin.H{"error": "unauthorized"})
		return
	}
	userID, err := uuid.Parse(claims.UserID)
	if err != nil {
		c.JSON(http.StatusBadRequest, gin.H{"error": "invalid user"})
		return
	}
	taskID, err := uuid.Parse(c.Param("id"))
	if err != nil {
		c.JSON(http.StatusBadRequest, gin.H{"error": "invalid task id"})
		return
	}

	ctx := c.Request.Context()
	cmdTag, err := h.Pool.Exec(ctx, `DELETE FROM tasks WHERE id = $1 AND user_id = $2`, taskID, userID)
	if err != nil {
		h.logger.Error("failed to delete task", "task_id", taskID, "user_id", userID, "error", err)
		c.JSON(http.StatusInternalServerError, gin.H{"error": "failed to delete task"})
		return
	}
	if cmdTag.RowsAffected() == 0 {
		c.JSON(http.StatusNotFound, gin.H{"error": "task not found"})
		return
	}
	h.logger.Info("task deleted", "task_id", taskID, "user_id", userID)
	c.Status(http.StatusNoContent)
}
