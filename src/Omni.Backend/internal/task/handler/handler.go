// Package handler wires the task HTTP routes to the TaskService.
// It contains no SQL or business logic — only request parsing, response
// mapping, and error translation.
package handler

import (
	"errors"
	"log/slog"
	"net/http"

	"omni-backend/internal/middleware"
	"omni-backend/internal/task/domain"
	"omni-backend/internal/task/service"

	"github.com/gin-gonic/gin"
	"github.com/google/uuid"
)

// Handler is the HTTP adapter for the task service.
type Handler struct {
	svc    service.TaskService
	logger *slog.Logger
}

// New returns a Handler wired to the given TaskService.
func New(svc service.TaskService, logger *slog.Logger) *Handler {
	return &Handler{svc: svc, logger: logger}
}

// RegisterRoutes attaches all task routes to rg.
// The router group should already have JWT middleware applied.
func (h *Handler) RegisterRoutes(rg *gin.RouterGroup) {
	rg.GET("", h.List)
	rg.POST("", h.Create)
	rg.PUT("/:id", h.Update)
	rg.PATCH("/:id/status", h.UpdateStatus)
	rg.DELETE("/:id", h.Delete)
}

// List godoc
// @Summary      List tasks
// @Tags         tasks
// @Security     BearerAuth
// @Produce      json
// @Success      200  {object}  ListResponse
// @Failure      401  {object}  errBody
// @Router       /tasks [get]
func (h *Handler) List(c *gin.Context) {
	userID := h.mustUserID(c)
	if userID == uuid.Nil {
		return
	}

	tasks, err := h.svc.ListTasks(c.Request.Context(), userID)
	if err != nil {
		h.handleErr(c, err)
		return
	}

	resp := make([]TaskResponse, len(tasks))
	for i, t := range tasks {
		resp[i] = toTaskResponse(t)
	}
	c.JSON(http.StatusOK, ListResponse{Tasks: resp})
}

// Create godoc
// @Summary      Create a task
// @Tags         tasks
// @Security     BearerAuth
// @Accept       json
// @Produce      json
// @Param        body  body      CreateTaskRequest  true  "Task payload"
// @Success      201   {object}  TaskResponse
// @Failure      400   {object}  errBody
// @Failure      401   {object}  errBody
// @Router       /tasks [post]
func (h *Handler) Create(c *gin.Context) {
	userID := h.mustUserID(c)
	if userID == uuid.Nil {
		return
	}

	var req CreateTaskRequest
	if err := c.ShouldBindJSON(&req); err != nil {
		c.JSON(http.StatusBadRequest, errResp("invalid request body"))
		return
	}

	cmd, err := req.toCommand(userID)
	if err != nil {
		c.JSON(http.StatusBadRequest, errResp(err.Error()))
		return
	}

	task, err := h.svc.CreateTask(c.Request.Context(), cmd)
	if err != nil {
		h.handleErr(c, err)
		return
	}

	c.JSON(http.StatusCreated, toTaskResponse(task))
}

// Update godoc
// @Summary      Update a task
// @Tags         tasks
// @Security     BearerAuth
// @Accept       json
// @Produce      json
// @Param        id    path      string            true  "Task ID"
// @Param        body  body      UpdateTaskRequest true  "Updated fields"
// @Success      200   {object}  TaskResponse
// @Failure      400   {object}  errBody
// @Failure      404   {object}  errBody
// @Router       /tasks/{id} [put]
func (h *Handler) Update(c *gin.Context) {
	userID := h.mustUserID(c)
	if userID == uuid.Nil {
		return
	}

	taskID, ok := h.parseTaskID(c)
	if !ok {
		return
	}

	var req UpdateTaskRequest
	if err := c.ShouldBindJSON(&req); err != nil {
		c.JSON(http.StatusBadRequest, errResp("invalid request body"))
		return
	}

	cmd, err := req.toCommand(taskID, userID)
	if err != nil {
		c.JSON(http.StatusBadRequest, errResp(err.Error()))
		return
	}

	task, err := h.svc.UpdateTask(c.Request.Context(), cmd)
	if err != nil {
		h.handleErr(c, err)
		return
	}

	c.JSON(http.StatusOK, toTaskResponse(task))
}

// UpdateStatus godoc
// @Summary      Change task status
// @Tags         tasks
// @Security     BearerAuth
// @Accept       json
// @Produce      json
// @Param        id    path      string              true  "Task ID"
// @Param        body  body      ChangeStatusRequest true  "New status"
// @Success      200   {object}  TaskResponse
// @Failure      400   {object}  errBody
// @Failure      404   {object}  errBody
// @Router       /tasks/{id}/status [patch]
func (h *Handler) UpdateStatus(c *gin.Context) {
	userID := h.mustUserID(c)
	if userID == uuid.Nil {
		return
	}

	taskID, ok := h.parseTaskID(c)
	if !ok {
		return
	}

	var req ChangeStatusRequest
	if err := c.ShouldBindJSON(&req); err != nil {
		c.JSON(http.StatusBadRequest, errResp("invalid request body"))
		return
	}

	task, err := h.svc.ChangeStatus(c.Request.Context(), service.ChangeStatusCmd{
		TaskID: taskID,
		UserID: userID,
		Status: req.Status,
	})
	if err != nil {
		h.handleErr(c, err)
		return
	}

	c.JSON(http.StatusOK, toTaskResponse(task))
}

// Delete godoc
// @Summary      Delete a task
// @Tags         tasks
// @Security     BearerAuth
// @Param        id   path      string  true  "Task ID"
// @Success      204  "No Content"
// @Failure      404  {object}  errBody
// @Router       /tasks/{id} [delete]
func (h *Handler) Delete(c *gin.Context) {
	userID := h.mustUserID(c)
	if userID == uuid.Nil {
		return
	}

	taskID, ok := h.parseTaskID(c)
	if !ok {
		return
	}

	if err := h.svc.DeleteTask(c.Request.Context(), taskID, userID); err != nil {
		h.handleErr(c, err)
		return
	}

	c.Status(http.StatusNoContent)
}

// ---- helpers ----

// mustUserID extracts the authenticated user ID from gin context.
// Responds with 401 and returns uuid.Nil if claims are missing.
func (h *Handler) mustUserID(c *gin.Context) uuid.UUID {
	claims := middleware.GetClaims(c)
	if claims == nil {
		c.JSON(http.StatusUnauthorized, errResp("unauthorized"))
		return uuid.Nil
	}
	id, err := uuid.Parse(claims.UserID)
	if err != nil {
		c.JSON(http.StatusUnauthorized, errResp("invalid user id in token"))
		return uuid.Nil
	}
	return id
}

// parseTaskID parses `:id` path param. Responds with 400 on failure.
func (h *Handler) parseTaskID(c *gin.Context) (uuid.UUID, bool) {
	id, err := uuid.Parse(c.Param("id"))
	if err != nil {
		c.JSON(http.StatusBadRequest, errResp("invalid task id"))
		return uuid.Nil, false
	}
	return id, true
}

// handleErr maps domain errors to HTTP status codes.
// All domain→HTTP mappings live here — one place to change.
func (h *Handler) handleErr(c *gin.Context, err error) {
	switch {
	case errors.Is(err, domain.ErrTaskNotFound):
		c.JSON(http.StatusNotFound, errResp(err.Error()))
	case errors.Is(err, domain.ErrTaskTitleEmpty),
		errors.Is(err, domain.ErrTaskTitleTooLong),
		errors.Is(err, domain.ErrInvalidPriority),
		errors.Is(err, domain.ErrInvalidStatus),
		errors.Is(err, domain.ErrInvalidDueDate),
		errors.Is(err, domain.ErrInvalidScheduledFor):
		c.JSON(http.StatusBadRequest, errResp(err.Error()))
	default:
		h.logger.ErrorContext(c.Request.Context(), "unexpected handler error", "error", err)
		c.JSON(http.StatusInternalServerError, errResp("internal server error"))
	}
}
