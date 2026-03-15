package telemetry

import (
	"encoding/json"
	"net/http"
	"strconv"
	"time"

	"omni-backend/internal/middleware"

	"github.com/gin-gonic/gin"
	"github.com/google/uuid"
	"github.com/jackc/pgx/v5/pgxpool"
)

// NotificationsHandler serves GET /api/productivity/notifications and PATCH .../:id/read.
type NotificationsHandler struct {
	Pool *pgxpool.Pool
}

// NewNotificationsHandler returns a handler that uses the given pool.
func NewNotificationsHandler(pool *pgxpool.Pool) *NotificationsHandler {
	return &NotificationsHandler{Pool: pool}
}

// NotificationItem is one row from user_notifications for JSON response.
type NotificationItem struct {
	ID            string          `json:"id"`
	CreatedAt     time.Time       `json:"created_at"`
	Type          string          `json:"type"`
	Title         string          `json:"title"`
	Body          string          `json:"body"`
	ActionType    string          `json:"action_type"`
	ActionPayload json.RawMessage `json:"action_payload,omitempty"`
	ReadAt        *time.Time      `json:"read_at,omitempty"`
}

// List godoc
// @Summary      List productivity notifications
// @Tags         productivity
// @Security     BearerAuth
// @Produce      json
// @Param        unread_only  query  bool  false  "If true, return only unread"
// @Success      200  {object}  map[string]interface{}
// @Failure      401  {object}  map[string]string
// @Router       /productivity/notifications [get]
func (h *NotificationsHandler) List(c *gin.Context) {
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

	unreadOnly := false
	if q := c.Query("unread_only"); q != "" {
		unreadOnly, _ = strconv.ParseBool(q)
	}

	ctx := c.Request.Context()
	query := `SELECT id, created_at, type, title, body, action_type, action_payload, read_at
		FROM user_notifications
		WHERE user_id = $1`
	args := []interface{}{userID}
	if unreadOnly {
		query += ` AND read_at IS NULL`
	}
	query += ` ORDER BY created_at DESC LIMIT 20`

	rows, err := h.Pool.Query(ctx, query, args...)
	if err != nil {
		c.JSON(http.StatusInternalServerError, gin.H{"error": "failed to load notifications"})
		return
	}
	defer rows.Close()

	var items []NotificationItem
	for rows.Next() {
		var id string
		var createdAt time.Time
		var notifType, title, body, actionType string
		var payload []byte
		var readAt *time.Time
		if err := rows.Scan(&id, &createdAt, &notifType, &title, &body, &actionType, &payload, &readAt); err != nil {
			continue
		}
		var payloadJSON json.RawMessage
		if len(payload) > 0 {
			payloadJSON = payload
		}
		items = append(items, NotificationItem{
			ID:            id,
			CreatedAt:     createdAt,
			Type:          notifType,
			Title:         title,
			Body:          body,
			ActionType:    actionType,
			ActionPayload: payloadJSON,
			ReadAt:        readAt,
		})
	}
	if items == nil {
		items = []NotificationItem{}
	}
	c.JSON(http.StatusOK, gin.H{"items": items})
}

// MarkRead godoc
// @Summary      Mark a notification as read
// @Tags         productivity
// @Security     BearerAuth
// @Param        id   path      string  true  "Notification ID"
// @Success      204  "No content"
// @Failure      401  {object}  map[string]string
// @Failure      404  {object}  map[string]string
// @Router       /productivity/notifications/{id}/read [patch]
func (h *NotificationsHandler) MarkRead(c *gin.Context) {
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
	notifID := c.Param("id")
	if notifID == "" {
		c.JSON(http.StatusBadRequest, gin.H{"error": "missing notification id"})
		return
	}
	if _, err := uuid.Parse(notifID); err != nil {
		c.JSON(http.StatusBadRequest, gin.H{"error": "invalid notification id"})
		return
	}

	ctx := c.Request.Context()
	cmd, err := h.Pool.Exec(ctx,
		`UPDATE user_notifications SET read_at = NOW() WHERE id = $1 AND user_id = $2`,
		notifID, userID)
	if err != nil {
		c.JSON(http.StatusInternalServerError, gin.H{"error": "failed to update"})
		return
	}
	if cmd.RowsAffected() == 0 {
		c.JSON(http.StatusNotFound, gin.H{"error": "not found"})
		return
	}
	c.Status(http.StatusNoContent)
}
