package handlers

import (
	"net/http"
	"time"

	"omni-backend/internal/middleware"

	"github.com/gin-gonic/gin"
	"github.com/google/uuid"
	"github.com/jackc/pgx/v5/pgxpool"
)

type SessionsHandler struct {
	Pool *pgxpool.Pool
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

func NewSessionsHandler(pool *pgxpool.Pool) *SessionsHandler {
	return &SessionsHandler{Pool: pool}
}

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
			c.JSON(http.StatusInternalServerError, gin.H{"error": "failed to save sessions"})
			return
		}
	}

	if err := tx.Commit(ctx); err != nil {
		c.JSON(http.StatusInternalServerError, gin.H{"error": "failed to save sessions"})
		return
	}
	c.JSON(http.StatusOK, gin.H{"ok": true})
}

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
	rows, err := h.Pool.Query(ctx,
		`SELECT id, name, activity_type, started_at, duration_seconds
		 FROM sessions
		 WHERE user_id = $1 AND date(started_at) >= $2 AND date(started_at) <= $3
		 ORDER BY started_at DESC`,
		userID, from, to)
	if err != nil {
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
