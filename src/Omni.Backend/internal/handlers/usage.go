package handlers

import (
	"fmt"
	"net/http"
	"strings"
	"time"

	"omni-backend/internal/middleware"

	"github.com/gin-gonic/gin"
	"github.com/google/uuid"
	"github.com/jackc/pgx/v5/pgxpool"
)

type UsageHandler struct {
	Pool *pgxpool.Pool
}

type syncEntry struct {
	AppName         string `json:"app_name"`
	Category        string `json:"category"`
	DurationSeconds int64  `json:"duration_seconds"`
}

type syncRequest struct {
	Entries []syncEntry `json:"entries"`
}

func NewUsageHandler(pool *pgxpool.Pool) *UsageHandler {
	return &UsageHandler{Pool: pool}
}

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
			c.JSON(http.StatusInternalServerError, gin.H{"error": "failed to save usage"})
			return
		}
	}
	if err := tx.Commit(ctx); err != nil {
		c.JSON(http.StatusInternalServerError, gin.H{"error": "failed to save usage"})
		return
	}
	c.JSON(http.StatusOK, gin.H{"ok": true})
}

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

	from := c.Query("from") // YYYY-MM-DD
	to := c.Query("to")
	if from == "" {
		from = time.Now().AddDate(0, 0, -30).Format("2006-01-02")
	}
	if to == "" {
		to = time.Now().Format("2006-01-02")
	}

	groupBy := c.Query("group_by") // day, week, month
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
		argNum++
	}
	query += fmt.Sprintf(
		` GROUP BY %s, app_name, category
		 ORDER BY period DESC, total_seconds DESC`,
		dateExpr)

	rows, err := h.Pool.Query(ctx, query, args...)
	if err != nil {
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
