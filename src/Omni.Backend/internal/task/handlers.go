package task

import (
	"net/http"
	"strings"

	"omni-backend/internal/middleware"

	"github.com/gin-gonic/gin"
	"github.com/google/uuid"
	"github.com/jackc/pgx/v5/pgxpool"
)

type Handler struct {
	Pool *pgxpool.Pool
}

func NewHandler(pool *pgxpool.Pool) *Handler {
	return &Handler{Pool: pool}
}

var allowedStatuses = map[string]bool{"pending": true, "done": true, "cancelled": true}

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
		`SELECT id::text, user_id::text, title, status, created_at::text, updated_at::text FROM tasks WHERE user_id = $1 ORDER BY created_at DESC`,
		userID)
	if err != nil {
		c.JSON(http.StatusInternalServerError, gin.H{"error": "failed to list tasks"})
		return
	}
	defer rows.Close()

	var tasks []TaskResponse
	for rows.Next() {
		var t TaskResponse
		if err := rows.Scan(&t.ID, &t.UserID, &t.Title, &t.Status, &t.CreatedAt, &t.UpdatedAt); err != nil {
			c.JSON(http.StatusInternalServerError, gin.H{"error": "failed to list tasks"})
			return
		}
		tasks = append(tasks, t)
	}
	if tasks == nil {
		tasks = []TaskResponse{}
	}
	c.JSON(http.StatusOK, ListResponse{Tasks: tasks})
}

// Create creates a new task for the authenticated user.
// Create godoc
// @Summary      Create a task
// @Tags         tasks
// @Security     BearerAuth
// @Accept       json
// @Produce      json
// @Param        body  body      CreateTaskRequest  true  "Task title and optional status"
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
	if status == "" {
		status = "pending"
	}
	if !allowedStatuses[status] {
		status = "pending"
	}

	ctx := c.Request.Context()
	var id string
	var createdAt, updatedAt string
	err = h.Pool.QueryRow(ctx,
		`INSERT INTO tasks (user_id, title, status) VALUES ($1, $2, $3) RETURNING id::text, created_at::text, updated_at::text`,
		userID, title, status).Scan(&id, &createdAt, &updatedAt)
	if err != nil {
		c.JSON(http.StatusInternalServerError, gin.H{"error": "failed to create task"})
		return
	}
	c.JSON(http.StatusCreated, TaskResponse{
		ID:        id,
		UserID:    userID.String(),
		Title:     title,
		Status:    status,
		CreatedAt: createdAt,
		UpdatedAt: updatedAt,
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
		c.JSON(http.StatusBadRequest, gin.H{"error": "status must be pending, done, or cancelled"})
		return
	}

	ctx := c.Request.Context()
	var id, title, createdAt, updatedAt string
	var uid uuid.UUID
	err = h.Pool.QueryRow(ctx,
		`UPDATE tasks SET status = $1, updated_at = NOW() WHERE id = $2 AND user_id = $3 RETURNING id::text, user_id, title, status, created_at::text, updated_at::text`,
		status, taskID, userID).Scan(&id, &uid, &title, &status, &createdAt, &updatedAt)
	if err != nil {
		if err.Error() == "no rows in result set" {
			c.JSON(http.StatusNotFound, gin.H{"error": "task not found"})
			return
		}
		c.JSON(http.StatusInternalServerError, gin.H{"error": "failed to update task"})
		return
	}
	c.JSON(http.StatusOK, TaskResponse{
		ID:        id,
		UserID:    uid.String(),
		Title:     title,
		Status:    status,
		CreatedAt: createdAt,
		UpdatedAt: updatedAt,
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
		c.JSON(http.StatusInternalServerError, gin.H{"error": "failed to delete task"})
		return
	}
	if cmdTag.RowsAffected() == 0 {
		c.JSON(http.StatusNotFound, gin.H{"error": "task not found"})
		return
	}
	c.Status(http.StatusNoContent)
}
