package calendar

import (
	"context"
	"log/slog"
	"net/http"
	"time"

	"omni-backend/internal/middleware"

	"github.com/gin-gonic/gin"
	"github.com/jackc/pgx/v5/pgxpool"
)

// Handler holds deps for the calendar service HTTP handlers.
type Handler struct {
	pool   *pgxpool.Pool
	google *GoogleClient
	syncer *Syncer
	logger *slog.Logger
}

func NewHandler(pool *pgxpool.Pool, google *GoogleClient, logger *slog.Logger) *Handler {
	syncer := NewSyncer(pool, google, logger)
	return &Handler{pool: pool, google: google, syncer: syncer, logger: logger}
}

// GetAuthURL returns the Google OAuth2 consent page URL.
func (h *Handler) GetAuthURL(c *gin.Context) {
	claims := middleware.GetClaims(c)
	if claims == nil {
		c.JSON(http.StatusUnauthorized, gin.H{"error": "unauthorized"})
		return
	}
	authURL := h.google.AuthURL(claims.UserID)
	c.JSON(http.StatusOK, AuthURLResponse{URL: authURL})
}

// Connect exchanges an OAuth code for tokens and stores them.
func (h *Handler) Connect(c *gin.Context) {
	claims := middleware.GetClaims(c)
	if claims == nil {
		c.JSON(http.StatusUnauthorized, gin.H{"error": "unauthorized"})
		return
	}

	var req ConnectRequest
	if err := c.ShouldBindJSON(&req); err != nil || req.Code == "" {
		c.JSON(http.StatusBadRequest, gin.H{"error": "code is required"})
		return
	}

	ctx := c.Request.Context()
	tr, err := h.google.ExchangeCode(ctx, req.Code)
	if err != nil {
		h.logger.Error("google code exchange failed", "user_id", claims.UserID, "error", err)
		c.JSON(http.StatusBadRequest, gin.H{"error": "invalid authorization code"})
		return
	}

	email, _ := h.google.GetUserEmail(ctx, tr.AccessToken)
	expiresAt := time.Now().Add(time.Duration(tr.ExpiresIn) * time.Second)

	_, err = h.pool.Exec(ctx,
		`INSERT INTO user_google_tokens (user_id, access_token, refresh_token, expires_at, email)
		 VALUES ($1, $2, $3, $4, $5)
		 ON CONFLICT (user_id) DO UPDATE
		   SET access_token = EXCLUDED.access_token,
		       refresh_token = CASE WHEN EXCLUDED.refresh_token != '' THEN EXCLUDED.refresh_token
		                            ELSE user_google_tokens.refresh_token END,
		       expires_at = EXCLUDED.expires_at,
		       email = EXCLUDED.email,
		       connected_at = NOW()`,
		claims.UserID, tr.AccessToken, tr.RefreshToken, expiresAt, email)
	if err != nil {
		h.logger.Error("failed to store google token", "user_id", claims.UserID, "error", err)
		c.JSON(http.StatusInternalServerError, gin.H{"error": "failed to connect"})
		return
	}

	// Kick off an initial sync in the background
	go func() {
		syncCtx := context.Background()
		if err := h.syncer.SyncForUser(syncCtx, claims.UserID); err != nil {
			h.logger.Warn("initial sync failed", "user_id", claims.UserID, "error", err)
		}
	}()

	h.logger.Info("google calendar connected", "user_id", claims.UserID, "email", email)
	c.JSON(http.StatusOK, gin.H{"connected": true, "email": email})
}

// Disconnect removes the stored Google OAuth tokens.
func (h *Handler) Disconnect(c *gin.Context) {
	claims := middleware.GetClaims(c)
	if claims == nil {
		c.JSON(http.StatusUnauthorized, gin.H{"error": "unauthorized"})
		return
	}

	ctx := c.Request.Context()
	_, err := h.pool.Exec(ctx,
		`DELETE FROM user_google_tokens WHERE user_id = $1`, claims.UserID)
	if err != nil {
		h.logger.Error("failed to disconnect google", "user_id", claims.UserID, "error", err)
		c.JSON(http.StatusInternalServerError, gin.H{"error": "failed to disconnect"})
		return
	}

	// Remove google_event_id references from tasks (keep tasks, just delink)
	_, _ = h.pool.Exec(ctx,
		`UPDATE tasks SET google_event_id = NULL WHERE user_id = $1`, claims.UserID)

	h.logger.Info("google calendar disconnected", "user_id", claims.UserID)
	c.JSON(http.StatusOK, gin.H{"connected": false})
}

// Status returns connection info for the current user.
func (h *Handler) Status(c *gin.Context) {
	claims := middleware.GetClaims(c)
	if claims == nil {
		c.JSON(http.StatusUnauthorized, gin.H{"error": "unauthorized"})
		return
	}

	ctx := c.Request.Context()
	var email string
	err := h.pool.QueryRow(ctx,
		`SELECT COALESCE(email,'') FROM user_google_tokens WHERE user_id = $1`,
		claims.UserID).Scan(&email)
	if err != nil {
		c.JSON(http.StatusOK, StatusResponse{Connected: false})
		return
	}

	// Find last sync time from calendar_events
	var lastSynced *string
	row := h.pool.QueryRow(ctx,
		`SELECT to_char(MAX(last_synced_at) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"')
		 FROM calendar_events WHERE user_id = $1`, claims.UserID)
	_ = row.Scan(&lastSynced)

	c.JSON(http.StatusOK, StatusResponse{
		Connected:    true,
		Email:        &email,
		LastSyncedAt: lastSynced,
	})
}

// ListEvents returns unified calendar events (tasks + GCal) for a date range.
// Query params: start (RFC3339), end (RFC3339).
func (h *Handler) ListEvents(c *gin.Context) {
	claims := middleware.GetClaims(c)
	if claims == nil {
		c.JSON(http.StatusUnauthorized, gin.H{"error": "unauthorized"})
		return
	}

	startStr := c.Query("start")
	endStr := c.Query("end")

	var start, end time.Time
	var err error
	if startStr == "" {
		// Default: current month start
		now := time.Now()
		start = time.Date(now.Year(), now.Month(), 1, 0, 0, 0, 0, time.UTC)
	} else {
		start, err = time.Parse(time.RFC3339, startStr)
		if err != nil {
			c.JSON(http.StatusBadRequest, gin.H{"error": "invalid start date"})
			return
		}
	}
	if endStr == "" {
		end = start.AddDate(0, 1, 0)
	} else {
		end, err = time.Parse(time.RFC3339, endStr)
		if err != nil {
			c.JSON(http.StatusBadRequest, gin.H{"error": "invalid end date"})
			return
		}
	}

	ctx := c.Request.Context()
	var events []CalendarEvent

	// 1. Fetch Omni tasks with due_date in range
	taskRows, err := h.pool.Query(ctx,
		`SELECT id::text, title, priority, status,
		        due_date,
		        to_char(due_date AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"'),
		        google_event_id::text
		 FROM tasks
		 WHERE user_id = $1 AND due_date >= $2 AND due_date < $3
		   AND status != 'cancelled'
		 ORDER BY due_date`,
		claims.UserID, start, end)
	if err != nil {
		h.logger.Error("failed to list task events", "user_id", claims.UserID, "error", err)
		c.JSON(http.StatusInternalServerError, gin.H{"error": "failed to list events"})
		return
	}
	defer taskRows.Close()

	for taskRows.Next() {
		var id, title, priority, status string
		var dueDate time.Time
		var dueDateStr string
		var googleEventID *string
		if err := taskRows.Scan(&id, &title, &priority, &status, &dueDate, &dueDateStr, &googleEventID); err != nil {
			continue
		}
		color := priorityColor(priority)
		events = append(events, CalendarEvent{
			ID:       "task-" + id,
			UserID:   claims.UserID,
			Title:    title,
			StartAt:  dueDate,
			IsAllDay: true,
			Source:   "omni_task",
			Priority: &priority,
			TaskStatus: &status,
			Color:    &color,
			OmniTaskID: &id,
		})
	}
	taskRows.Close()

	// 2. Fetch GCal events in range (already synced into calendar_events table)
	gcalRows, err := h.pool.Query(ctx,
		`SELECT id::text, COALESCE(google_event_id,''), title,
		        COALESCE(description,''), start_at, end_at, is_all_day,
		        COALESCE(google_calendar_id,''), COALESCE(color,'')
		 FROM calendar_events
		 WHERE user_id = $1 AND start_at >= $2 AND start_at < $3
		 ORDER BY start_at`,
		claims.UserID, start, end)
	if err != nil {
		h.logger.Error("failed to list gcal events", "user_id", claims.UserID, "error", err)
		c.JSON(http.StatusInternalServerError, gin.H{"error": "failed to list events"})
		return
	}
	defer gcalRows.Close()

	for gcalRows.Next() {
		var id, googleEventID, title, description, calendarID, color string
		var startAt time.Time
		var endAt *time.Time
		var isAllDay bool
		if err := gcalRows.Scan(&id, &googleEventID, &title, &description, &startAt, &endAt, &isAllDay, &calendarID, &color); err != nil {
			continue
		}
		var geid *string
		if googleEventID != "" {
			geid = &googleEventID
		}
		var desc *string
		if description != "" {
			desc = &description
		}
		var calID *string
		if calendarID != "" {
			calID = &calendarID
		}
		gcalColor := "#4A90E2" // default blue for GCal
		if color != "" {
			gcalColor = colorIDToHex(color)
		}
		events = append(events, CalendarEvent{
			ID:               "gcal-" + id,
			UserID:           claims.UserID,
			GoogleEventID:    geid,
			Title:            title,
			Description:      desc,
			StartAt:          startAt,
			EndAt:            endAt,
			IsAllDay:         isAllDay,
			GoogleCalendarID: calID,
			Color:            &gcalColor,
			Source:           "google_calendar",
		})
	}

	if events == nil {
		events = []CalendarEvent{}
	}
	c.JSON(http.StatusOK, EventsResponse{Events: events})
}

// Sync triggers a manual sync for the authenticated user.
func (h *Handler) Sync(c *gin.Context) {
	claims := middleware.GetClaims(c)
	if claims == nil {
		c.JSON(http.StatusUnauthorized, gin.H{"error": "unauthorized"})
		return
	}

	ctx := c.Request.Context()
	if err := h.syncer.SyncForUser(ctx, claims.UserID); err != nil {
		h.logger.Warn("manual sync failed", "user_id", claims.UserID, "error", err)
		c.JSON(http.StatusOK, gin.H{"synced": false, "error": err.Error()})
		return
	}
	c.JSON(http.StatusOK, gin.H{"synced": true})
}

func priorityColor(priority string) string {
	switch priority {
	case "high":
		return "#FF5C5C"
	case "low":
		return "#4ECCA3"
	default:
		return "#F5A623"
	}
}

// colorIDToHex maps Google Calendar colorId to a hex color.
func colorIDToHex(colorID string) string {
	colors := map[string]string{
		"1": "#A4BDFC", "2": "#7AE7BF", "3": "#DBADFF", "4": "#FF887C",
		"5": "#FBD75B", "6": "#FFB878", "7": "#46D6DB", "8": "#E1E1E1",
		"9": "#5484ED", "10": "#51B749", "11": "#DC2127",
	}
	if c, ok := colors[colorID]; ok {
		return c
	}
	return "#4A90E2"
}
