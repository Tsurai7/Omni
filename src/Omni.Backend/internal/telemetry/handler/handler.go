// Package handler wires telemetry HTTP routes to the TelemetryService.
package handler

import (
	"errors"
	"log/slog"
	"net/http"
	"strconv"
	"time"

	"omni-backend/internal/localdate"
	"omni-backend/internal/middleware"
	tdomain "omni-backend/internal/telemetry/domain"
	"omni-backend/internal/telemetry/service"

	"github.com/gin-gonic/gin"
	"github.com/google/uuid"
)

// Handler is the HTTP adapter for the telemetry service.
type Handler struct {
	svc    service.TelemetryService
	logger *slog.Logger
}

// New returns a Handler wired to the given TelemetryService.
func New(svc service.TelemetryService, logger *slog.Logger) *Handler {
	return &Handler{svc: svc, logger: logger}
}

// RegisterRoutes attaches all telemetry routes to the given groups.
func (h *Handler) RegisterRoutes(usageGrp, sessionsGrp, productivityGrp *gin.RouterGroup) {
	usageGrp.POST("/sync", h.SyncUsage)
	usageGrp.GET("", h.ListUsage)
	sessionsGrp.POST("/sync", h.SyncSessions)
	sessionsGrp.GET("", h.ListSessions)
	productivityGrp.GET("/notifications", h.ListNotifications)
	productivityGrp.PATCH("/notifications/:id/read", h.MarkNotificationRead)
}

// ---- request types ----

type usageSyncEntry struct {
	AppName         string `json:"app_name"`
	Category        string `json:"category"`
	DurationSeconds int64  `json:"duration_seconds"`
}

type usageSyncRequest struct {
	Entries []usageSyncEntry `json:"entries" binding:"required"`
}

type sessionSyncEntry struct {
	Name            string `json:"name"`
	ActivityType    string `json:"activity_type"`
	StartedAt       string `json:"started_at"`
	DurationSeconds int64  `json:"duration_seconds"`
}

type sessionSyncRequest struct {
	Entries []sessionSyncEntry `json:"entries" binding:"required"`
}

type errBody struct {
	Error string `json:"error"`
}

func errResp(msg string) errBody { return errBody{Error: msg} }

// ---- handlers ----

// SyncUsage godoc
// @Summary      Sync usage entries
// @Tags         usage
// @Security     BearerAuth
// @Accept       json
// @Produce      json
// @Param        body  body  usageSyncRequest  true  "Usage entries"
// @Success      200   {object}  map[string]bool
// @Failure      400   {object}  errBody
// @Router       /usage/sync [post]
func (h *Handler) SyncUsage(c *gin.Context) {
	userID, ok := h.mustUserID(c)
	if !ok {
		return
	}

	var req usageSyncRequest
	if err := c.ShouldBindJSON(&req); err != nil {
		c.JSON(http.StatusBadRequest, errResp("invalid request body"))
		return
	}

	entries := make([]service.UsageEntry, len(req.Entries))
	for i, e := range req.Entries {
		entries[i] = service.UsageEntry{
			AppName:         e.AppName,
			Category:        e.Category,
			DurationSeconds: e.DurationSeconds,
		}
	}

	if err := h.svc.SyncUsage(c.Request.Context(), service.SyncUsageCmd{
		UserID:  userID,
		Entries: entries,
	}); err != nil {
		h.handleErr(c, err)
		return
	}

	c.JSON(http.StatusOK, gin.H{"ok": true})
}

// ListUsage godoc
// @Summary      List usage records
// @Tags         usage
// @Security     BearerAuth
// @Produce      json
// @Param        from      query  string  false  "From date (YYYY-MM-DD)"
// @Param        to        query  string  false  "To date (YYYY-MM-DD)"
// @Param        group_by  query  string  false  "day|week|month"
// @Success      200  {object}  map[string]interface{}
// @Router       /usage [get]
func (h *Handler) ListUsage(c *gin.Context) {
	userID, ok := h.mustUserID(c)
	if !ok {
		return
	}

	from, to := dateRange(c)
	groupBy := c.DefaultQuery("group_by", "day")
	if groupBy != "day" && groupBy != "week" && groupBy != "month" {
		groupBy = "day"
	}

	aggs, err := h.svc.ListUsage(c.Request.Context(), tdomain.UsageQuery{
		UserID:           userID,
		From:             from,
		To:               to,
		GroupBy:          groupBy,
		CategoryFilter:   c.Query("category"),
		AppFilter:        c.Query("app_name"),
		UTCOffsetMinutes: localdate.OffsetMinutes(c),
	})
	if err != nil {
		h.handleErr(c, err)
		return
	}

	type row struct {
		Period       string `json:"date"`
		AppName      string `json:"app_name"`
		Category     string `json:"category"`
		TotalSeconds int64  `json:"total_seconds"`
	}
	out := make([]row, len(aggs))
	for i, a := range aggs {
		out[i] = row{
			Period:       a.Period,
			AppName:      a.AppName,
			Category:     a.Category,
			TotalSeconds: a.TotalSeconds,
		}
	}
	c.JSON(http.StatusOK, gin.H{"entries": out})
}

// SyncSessions godoc
// @Summary      Sync session entries
// @Tags         sessions
// @Security     BearerAuth
// @Accept       json
// @Produce      json
// @Param        body  body  sessionSyncRequest  true  "Session entries"
// @Success      200   {object}  map[string]bool
// @Failure      400   {object}  errBody
// @Router       /sessions/sync [post]
func (h *Handler) SyncSessions(c *gin.Context) {
	userID, ok := h.mustUserID(c)
	if !ok {
		return
	}

	var req sessionSyncRequest
	if err := c.ShouldBindJSON(&req); err != nil {
		c.JSON(http.StatusBadRequest, errResp("invalid request body"))
		return
	}

	entries := make([]service.SessionEntry, len(req.Entries))
	for i, e := range req.Entries {
		entries[i] = service.SessionEntry{
			Name:            e.Name,
			ActivityType:    e.ActivityType,
			StartedAt:       e.StartedAt,
			DurationSeconds: e.DurationSeconds,
		}
	}

	if err := h.svc.SyncSessions(c.Request.Context(), service.SyncSessionsCmd{
		UserID:  userID,
		Entries: entries,
	}); err != nil {
		h.handleErr(c, err)
		return
	}

	c.JSON(http.StatusOK, gin.H{"ok": true})
}

// ListSessions godoc
// @Summary      List sessions
// @Tags         sessions
// @Security     BearerAuth
// @Produce      json
// @Param        from  query  string  false  "From date (YYYY-MM-DD)"
// @Param        to    query  string  false  "To date (YYYY-MM-DD)"
// @Success      200  {object}  map[string]interface{}
// @Router       /sessions [get]
func (h *Handler) ListSessions(c *gin.Context) {
	userID, ok := h.mustUserID(c)
	if !ok {
		return
	}

	from, to := dateRange(c)

	sessions, err := h.svc.ListSessions(c.Request.Context(), tdomain.SessionQuery{
		UserID:           userID,
		From:             from,
		To:               to,
		UTCOffsetMinutes: localdate.OffsetMinutes(c),
	})
	if err != nil {
		h.handleErr(c, err)
		return
	}

	type row struct {
		ID              string `json:"id"`
		Name            string `json:"name"`
		ActivityType    string `json:"activity_type"`
		StartedAt       string `json:"started_at"`
		DurationSeconds int64  `json:"duration_seconds"`
	}
	out := make([]row, len(sessions))
	for i, s := range sessions {
		out[i] = row{
			ID:              s.ID.String(),
			Name:            s.Name,
			ActivityType:    s.ActivityType,
			StartedAt:       s.StartedAt.UTC().Format(time.RFC3339),
			DurationSeconds: s.DurationSeconds,
		}
	}
	c.JSON(http.StatusOK, gin.H{"entries": out})
}

// ListNotifications godoc
// @Summary      List productivity notifications
// @Tags         productivity
// @Security     BearerAuth
// @Produce      json
// @Param        unread_only  query  bool  false  "Return only unread"
// @Success      200  {object}  map[string]interface{}
// @Router       /productivity/notifications [get]
func (h *Handler) ListNotifications(c *gin.Context) {
	userID, ok := h.mustUserID(c)
	if !ok {
		return
	}

	unreadOnly, _ := strconv.ParseBool(c.Query("unread_only"))

	items, err := h.svc.ListNotifications(c.Request.Context(), userID, unreadOnly)
	if err != nil {
		h.handleErr(c, err)
		return
	}

	c.JSON(http.StatusOK, gin.H{"items": items})
}

// MarkNotificationRead godoc
// @Summary      Mark notification as read
// @Tags         productivity
// @Security     BearerAuth
// @Param        id   path  string  true  "Notification ID"
// @Success      204  "No content"
// @Failure      404  {object}  errBody
// @Router       /productivity/notifications/{id}/read [patch]
func (h *Handler) MarkNotificationRead(c *gin.Context) {
	userID, ok := h.mustUserID(c)
	if !ok {
		return
	}

	notifID, err := uuid.Parse(c.Param("id"))
	if err != nil {
		c.JSON(http.StatusBadRequest, errResp("invalid notification id"))
		return
	}

	if err := h.svc.MarkNotificationRead(c.Request.Context(), notifID, userID); err != nil {
		h.handleErr(c, err)
		return
	}

	c.Status(http.StatusNoContent)
}

// ---- helpers ----

func (h *Handler) mustUserID(c *gin.Context) (uuid.UUID, bool) {
	claims := middleware.GetClaims(c)
	if claims == nil {
		c.JSON(http.StatusUnauthorized, errResp("unauthorized"))
		return uuid.Nil, false
	}
	id, err := uuid.Parse(claims.UserID)
	if err != nil {
		c.JSON(http.StatusUnauthorized, errResp("invalid user id in token"))
		return uuid.Nil, false
	}
	return id, true
}

func (h *Handler) handleErr(c *gin.Context, err error) {
	switch {
	case errors.Is(err, tdomain.ErrNotificationNotFound):
		c.JSON(http.StatusNotFound, errResp(err.Error()))
	case errors.Is(err, tdomain.ErrInvalidStartedAt),
		errors.Is(err, tdomain.ErrNegativeDuration),
		errors.Is(err, tdomain.ErrAppNameEmpty),
		errors.Is(err, tdomain.ErrSessionNameEmpty):
		c.JSON(http.StatusBadRequest, errResp(err.Error()))
	default:
		h.logger.ErrorContext(c.Request.Context(), "telemetry handler error", "error", err)
		c.JSON(http.StatusInternalServerError, errResp("internal server error"))
	}
}

func dateRange(c *gin.Context) (from, to string) {
	from = c.Query("from")
	to = c.Query("to")
	if from == "" {
		from = time.Now().AddDate(0, 0, -30).Format("2006-01-02")
	}
	if to == "" {
		to = time.Now().Format("2006-01-02")
	}
	return
}
