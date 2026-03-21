package telemetry

import (
	"fmt"
	"log/slog"
	"net/http"
	"strings"
	"time"

	"omni-backend/internal/middleware"

	"github.com/gin-gonic/gin"
	"github.com/google/uuid"
	"github.com/jackc/pgx/v5/pgxpool"
)

type UsageHandler struct {
	Pool      *pgxpool.Pool
	Publisher Publisher
	logger    *slog.Logger
}

type syncEntry struct {
	AppName         string `json:"app_name"`
	Category        string `json:"category"`
	DurationSeconds int64  `json:"duration_seconds"`
}

type syncRequest struct {
	Entries []syncEntry `json:"entries"`
}

func NewUsageHandler(pool *pgxpool.Pool, pub Publisher, logger *slog.Logger) *UsageHandler {
	return &UsageHandler{Pool: pool, Publisher: pub, logger: logger}
}

// Sync godoc
// @Summary      Sync usage entries
// @Tags         usage
// @Security     BearerAuth
// @Accept       json
// @Produce      json
// @Param        body  body  syncRequest  true  "Usage entries"
// @Success      200   {object}  map[string]interface{}
// @Failure      400   {object}  map[string]string
// @Failure      401   {object}  map[string]string
// @Router       /usage/sync [post]
func (h *UsageHandler) Sync(c *gin.Context) {
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

	var req syncRequest
	if err := c.ShouldBindJSON(&req); err != nil {
		c.JSON(http.StatusBadRequest, gin.H{"error": "invalid request body"})
		return
	}
	if len(req.Entries) == 0 {
		c.JSON(http.StatusOK, gin.H{"ok": true})
		return
	}

	ctx := c.Request.Context()
	now := time.Now().UTC()
	tx, err := h.Pool.Begin(ctx)
	if err != nil {
		h.logger.Error("failed to begin usage sync transaction", "user_id", userID, "error", err)
		c.JSON(http.StatusInternalServerError, gin.H{"error": "failed to save usage"})
		return
	}
	defer tx.Rollback(ctx)
	for _, e := range req.Entries {
		if e.AppName == "" || e.DurationSeconds <= 0 {
			continue
		}
		category := e.Category
		if category == "" {
			category = "Other"
		}
		_, err := tx.Exec(ctx,
			`INSERT INTO usage_records (user_id, app_name, category, duration_seconds, recorded_at) VALUES ($1, $2, $3, $4, $5)`,
			userID, e.AppName, category, e.DurationSeconds, now)
		if err != nil {
			h.logger.Error("failed to insert usage record", "user_id", userID, "app_name", e.AppName, "error", err)
			c.JSON(http.StatusInternalServerError, gin.H{"error": "failed to save usage"})
			return
		}
	}
	if err := tx.Commit(ctx); err != nil {
		h.logger.Error("failed to commit usage sync", "user_id", userID, "error", err)
		c.JSON(http.StatusInternalServerError, gin.H{"error": "failed to save usage"})
		return
	}
	h.logger.Info("usage synced", "user_id", userID, "entries", len(req.Entries))
	if h.Publisher != nil {
		for _, e := range req.Entries {
			if e.AppName == "" || e.DurationSeconds <= 0 {
				continue
			}
			category := e.Category
			if category == "" {
				category = "Other"
			}
			h.Publisher.PublishUsage(userID, e.AppName, category, e.DurationSeconds, now)
		}
	}
	c.JSON(http.StatusOK, gin.H{"ok": true})
}

// List godoc
// @Summary      List usage entries
// @Tags         usage
// @Security     BearerAuth
// @Produce      json
// @Param        from       query  string  false  "From date (YYYY-MM-DD)"
// @Param        to         query  string  false  "To date (YYYY-MM-DD)"
// @Param        group_by   query  string  false  "day|week|month"
// @Param        category  query  string  false  "Filter by category"
// @Param        app_name  query  string  false  "Filter by app name"
// @Success      200  {object}  map[string]interface{}
// @Failure      401  {object}  map[string]string
// @Router       /usage [get]
func (h *UsageHandler) List(c *gin.Context) {
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

	from := c.Query("from")
	to := c.Query("to")
	if from == "" {
		from = time.Now().AddDate(0, 0, -30).Format("2006-01-02")
	}
	if to == "" {
		to = time.Now().Format("2006-01-02")
	}

	groupBy := c.Query("group_by")
	if groupBy == "" {
		groupBy = "day"
	}
	if groupBy != "day" && groupBy != "week" && groupBy != "month" {
		groupBy = "day"
	}

	categoryFilter := strings.TrimSpace(c.Query("category"))
	appFilter := strings.TrimSpace(c.Query("app_name"))

	ctx := c.Request.Context()

	var dateExpr string
	switch groupBy {
	case "week":
		dateExpr = "to_char(recorded_at, 'IYYY-\"W\"IW')"
	case "month":
		dateExpr = "to_char(recorded_at, 'YYYY-MM')"
	default:
		dateExpr = "to_char(date(recorded_at), 'YYYY-MM-DD')"
	}

	query := fmt.Sprintf(
		`SELECT %s AS period, app_name, category, SUM(duration_seconds) AS total_seconds
		 FROM usage_records
		 WHERE user_id = $1 AND date(recorded_at) >= $2 AND date(recorded_at) <= $3`,
		dateExpr)
	args := []interface{}{userID, from, to}
	argNum := 4
	if categoryFilter != "" {
		query += fmt.Sprintf(" AND category = $%d", argNum)
		args = append(args, categoryFilter)
		argNum++
	}
	if appFilter != "" {
		query += fmt.Sprintf(" AND app_name = $%d", argNum)
		args = append(args, appFilter)
	}
	query += fmt.Sprintf(
		` GROUP BY %s, app_name, category
		 ORDER BY period DESC, total_seconds DESC`,
		dateExpr)

	h.logger.Debug("listing usage", "user_id", userID, "from", from, "to", to, "group_by", groupBy)
	rows, err := h.Pool.Query(ctx, query, args...)
	if err != nil {
		h.logger.Error("failed to query usage records", "user_id", userID, "error", err)
		c.JSON(http.StatusInternalServerError, gin.H{"error": "failed to load usage"})
		return
	}
	defer rows.Close()

	type row struct {
		Period       string `json:"date"`
		AppName      string `json:"app_name"`
		Category     string `json:"category"`
		TotalSeconds int64  `json:"total_seconds"`
	}
	var list []row
	for rows.Next() {
		var r row
		if err := rows.Scan(&r.Period, &r.AppName, &r.Category, &r.TotalSeconds); err != nil {
			continue
		}
		list = append(list, r)
	}
	if list == nil {
		list = []row{}
	}
	c.JSON(http.StatusOK, gin.H{"entries": list})
}

type SessionsHandler struct {
	Pool      *pgxpool.Pool
	Publisher Publisher
	logger    *slog.Logger
}

type sessionSyncEntry struct {
	Name            string `json:"name"`
	ActivityType    string `json:"activity_type"`
	StartedAt       string `json:"started_at"`
	DurationSeconds int64  `json:"duration_seconds"`
}

type sessionSyncRequest struct {
	Entries []sessionSyncEntry `json:"entries"`
}

func NewSessionsHandler(pool *pgxpool.Pool, pub Publisher, logger *slog.Logger) *SessionsHandler {
	return &SessionsHandler{Pool: pool, Publisher: pub, logger: logger}
}

// Sync godoc
// @Summary      Sync session entries
// @Tags         sessions
// @Security     BearerAuth
// @Accept       json
// @Produce      json
// @Param        body  body  sessionSyncRequest  true  "Session entries"
// @Success      200   {object}  map[string]interface{}
// @Failure      400   {object}  map[string]string
// @Failure      401   {object}  map[string]string
// @Router       /sessions/sync [post]
func (h *SessionsHandler) Sync(c *gin.Context) {
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

	var req sessionSyncRequest
	if err := c.ShouldBindJSON(&req); err != nil {
		c.JSON(http.StatusBadRequest, gin.H{"error": "invalid request body"})
		return
	}
	if len(req.Entries) == 0 {
		c.JSON(http.StatusOK, gin.H{"ok": true})
		return
	}

	ctx := c.Request.Context()
	tx, err := h.Pool.Begin(ctx)
	if err != nil {
		h.logger.Error("failed to begin sessions sync transaction", "user_id", userID, "error", err)
		c.JSON(http.StatusInternalServerError, gin.H{"error": "failed to save sessions"})
		return
	}
	defer tx.Rollback(ctx)

	for _, e := range req.Entries {
		if e.Name == "" || e.DurationSeconds <= 0 {
			continue
		}
		startedAt, err := time.Parse(time.RFC3339, e.StartedAt)
		if err != nil {
			h.logger.Warn("invalid started_at in session entry", "user_id", userID, "value", e.StartedAt)
			c.JSON(http.StatusBadRequest, gin.H{"error": "invalid started_at format"})
			return
		}
		activityType := e.ActivityType
		if activityType == "" {
			activityType = "other"
		}
		_, err = tx.Exec(ctx,
			`INSERT INTO sessions (user_id, name, activity_type, started_at, duration_seconds) VALUES ($1, $2, $3, $4, $5)`,
			userID, e.Name, activityType, startedAt.UTC(), e.DurationSeconds)
		if err != nil {
			h.logger.Error("failed to insert session record", "user_id", userID, "name", e.Name, "error", err)
			c.JSON(http.StatusInternalServerError, gin.H{"error": "failed to save sessions"})
			return
		}
	}

	if err := tx.Commit(ctx); err != nil {
		h.logger.Error("failed to commit sessions sync", "user_id", userID, "error", err)
		c.JSON(http.StatusInternalServerError, gin.H{"error": "failed to save sessions"})
		return
	}
	h.logger.Info("sessions synced", "user_id", userID, "entries", len(req.Entries))
	if h.Publisher != nil {
		for _, e := range req.Entries {
			if e.Name == "" || e.DurationSeconds <= 0 {
				continue
			}
			startedAt, _ := time.Parse(time.RFC3339, e.StartedAt)
			activityType := e.ActivityType
			if activityType == "" {
				activityType = "other"
			}
			h.Publisher.PublishSession(userID, e.Name, activityType, startedAt.UTC(), e.DurationSeconds)
		}
	}
	c.JSON(http.StatusOK, gin.H{"ok": true})
}

// List godoc
// @Summary      List sessions
// @Tags         sessions
// @Security     BearerAuth
// @Produce      json
// @Param        from  query  string  false  "From date (YYYY-MM-DD)"
// @Param        to    query  string  false  "To date (YYYY-MM-DD)"
// @Success      200  {object}  map[string]interface{}
// @Failure      401  {object}  map[string]string
// @Router       /sessions [get]
func (h *SessionsHandler) List(c *gin.Context) {
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

	from := c.Query("from")
	to := c.Query("to")
	if from == "" {
		from = time.Now().AddDate(0, 0, -30).Format("2006-01-02")
	}
	if to == "" {
		to = time.Now().Format("2006-01-02")
	}

	ctx := c.Request.Context()
	h.logger.Debug("listing sessions", "user_id", userID, "from", from, "to", to)
	rows, err := h.Pool.Query(ctx,
		`SELECT id, name, activity_type, started_at, duration_seconds
		 FROM sessions
		 WHERE user_id = $1 AND date(started_at) >= $2 AND date(started_at) <= $3
		 ORDER BY started_at DESC`,
		userID, from, to)
	if err != nil {
		h.logger.Error("failed to query sessions", "user_id", userID, "error", err)
		c.JSON(http.StatusInternalServerError, gin.H{"error": "failed to load sessions"})
		return
	}
	defer rows.Close()

	type row struct {
		ID              string `json:"id"`
		Name            string `json:"name"`
		ActivityType    string `json:"activity_type"`
		StartedAt       string `json:"started_at"`
		DurationSeconds int64  `json:"duration_seconds"`
	}
	var list []row
	for rows.Next() {
		var r row
		var startedAt time.Time
		if err := rows.Scan(&r.ID, &r.Name, &r.ActivityType, &startedAt, &r.DurationSeconds); err != nil {
			continue
		}
		r.StartedAt = startedAt.UTC().Format(time.RFC3339)
		list = append(list, r)
	}
	if list == nil {
		list = []row{}
	}
	c.JSON(http.StatusOK, gin.H{"entries": list})
}
