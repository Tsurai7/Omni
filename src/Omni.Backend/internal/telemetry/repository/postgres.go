// Package repository implements telemetry persistence against PostgreSQL.
package repository

import (
	"context"
	"fmt"
	"strings"

	tdomain "omni-backend/internal/telemetry/domain"

	"github.com/google/uuid"
	"github.com/jackc/pgx/v5/pgxpool"

	"omni-backend/internal/localdate"
)

// ---- UsageRepository ----

type postgresUsageRepo struct{ pool *pgxpool.Pool }

// NewPostgresUsage returns a UsageRepository backed by PostgreSQL.
func NewPostgresUsage(pool *pgxpool.Pool) tdomain.UsageRepository {
	return &postgresUsageRepo{pool: pool}
}

func (r *postgresUsageRepo) BulkInsert(ctx context.Context, records []*tdomain.UsageRecord) error {
	tx, err := r.pool.Begin(ctx)
	if err != nil {
		return err
	}
	defer tx.Rollback(ctx) //nolint:errcheck

	for _, rec := range records {
		_, err := tx.Exec(ctx,
			`INSERT INTO usage_records (user_id, app_name, category, duration_seconds, recorded_at)
			 VALUES ($1, $2, $3, $4, $5)`,
			rec.UserID, rec.AppName, rec.Category, rec.DurationSeconds, rec.RecordedAt)
		if err != nil {
			return err
		}
	}
	return tx.Commit(ctx)
}

func (r *postgresUsageRepo) List(ctx context.Context, q tdomain.UsageQuery) ([]*tdomain.UsageAggregate, error) {
	localRec := localdate.SQLExpr("recorded_at", 2)

	groupBy := q.GroupBy
	if groupBy == "" {
		groupBy = "day"
	}
	var periodExpr string
	switch groupBy {
	case "week":
		periodExpr = fmt.Sprintf(`to_char(%s, 'IYYY-"W"IW')`, localRec)
	case "month":
		periodExpr = fmt.Sprintf(`to_char(%s, 'YYYY-MM')`, localRec)
	default:
		periodExpr = fmt.Sprintf(`to_char(%s, 'YYYY-MM-DD')`, localRec)
	}

	query := fmt.Sprintf(
		`SELECT %s AS period, app_name, category, SUM(duration_seconds) AS total_seconds
		 FROM usage_records
		 WHERE user_id = $1 AND %s >= $3::date AND %s <= $4::date`,
		periodExpr, localRec, localRec)

	args := []any{q.UserID, q.UTCOffsetMinutes, q.From, q.To}
	argNum := 5
	if q.CategoryFilter != "" {
		query += fmt.Sprintf(" AND category = $%d", argNum)
		args = append(args, q.CategoryFilter)
		argNum++
	}
	if q.AppFilter != "" {
		query += fmt.Sprintf(" AND app_name = $%d", argNum)
		args = append(args, q.AppFilter)
	}
	query += fmt.Sprintf(` GROUP BY %s, app_name, category ORDER BY period DESC, total_seconds DESC`, periodExpr)

	rows, err := r.pool.Query(ctx, query, args...)
	if err != nil {
		return nil, err
	}
	defer rows.Close()

	var result []*tdomain.UsageAggregate
	for rows.Next() {
		var a tdomain.UsageAggregate
		if err := rows.Scan(&a.Period, &a.AppName, &a.Category, &a.TotalSeconds); err != nil {
			return nil, err
		}
		result = append(result, &a)
	}
	if result == nil {
		result = []*tdomain.UsageAggregate{}
	}
	return result, rows.Err()
}

// ---- SessionRepository ----

type postgresSessionRepo struct{ pool *pgxpool.Pool }

// NewPostgresSession returns a SessionRepository backed by PostgreSQL.
func NewPostgresSession(pool *pgxpool.Pool) tdomain.SessionRepository {
	return &postgresSessionRepo{pool: pool}
}

func (r *postgresSessionRepo) BulkInsert(ctx context.Context, sessions []*tdomain.Session) error {
	tx, err := r.pool.Begin(ctx)
	if err != nil {
		return err
	}
	defer tx.Rollback(ctx) //nolint:errcheck

	for _, s := range sessions {
		_, err := tx.Exec(ctx,
			`INSERT INTO sessions (user_id, name, activity_type, started_at, duration_seconds)
			 VALUES ($1, $2, $3, $4, $5)`,
			s.UserID, s.Name, s.ActivityType, s.StartedAt, s.DurationSeconds)
		if err != nil {
			return err
		}
	}
	return tx.Commit(ctx)
}

func (r *postgresSessionRepo) List(ctx context.Context, q tdomain.SessionQuery) ([]*tdomain.Session, error) {
	localStarted := localdate.SQLExpr("started_at", 2)

	query := fmt.Sprintf(
		`SELECT id, name, activity_type, started_at, duration_seconds
		 FROM sessions
		 WHERE user_id = $1 AND %s >= $3::date AND %s <= $4::date
		 ORDER BY started_at DESC`,
		localStarted, localStarted)

	rows, err := r.pool.Query(ctx, query, q.UserID, q.UTCOffsetMinutes, q.From, q.To)
	if err != nil {
		return nil, err
	}
	defer rows.Close()

	var result []*tdomain.Session
	for rows.Next() {
		var s tdomain.Session
		s.UserID = q.UserID
		if err := rows.Scan(&s.ID, &s.Name, &s.ActivityType, &s.StartedAt, &s.DurationSeconds); err != nil {
			return nil, err
		}
		result = append(result, &s)
	}
	if result == nil {
		result = []*tdomain.Session{}
	}
	return result, rows.Err()
}

// ---- NotificationRepository ----

type postgresNotifRepo struct{ pool *pgxpool.Pool }

// NewPostgresNotification returns a NotificationRepository backed by PostgreSQL.
func NewPostgresNotification(pool *pgxpool.Pool) tdomain.NotificationRepository {
	return &postgresNotifRepo{pool: pool}
}

func (r *postgresNotifRepo) List(ctx context.Context, userID uuid.UUID, unreadOnly bool) ([]*tdomain.Notification, error) {
	q := `SELECT id, created_at, type,
	             COALESCE(title,''), COALESCE(body,''), COALESCE(action_type,''),
	             action_payload, read_at
	      FROM user_notifications
	      WHERE user_id = $1`
	args := []any{userID}
	if unreadOnly {
		q += ` AND read_at IS NULL`
	}
	q += ` ORDER BY created_at DESC LIMIT 20`

	rows, err := r.pool.Query(ctx, q, args...)
	if err != nil {
		return nil, err
	}
	defer rows.Close()

	var result []*tdomain.Notification
	for rows.Next() {
		var n tdomain.Notification
		n.UserID = userID
		if err := rows.Scan(&n.ID, &n.CreatedAt, &n.Type,
			&n.Title, &n.Body, &n.ActionType,
			&n.ActionPayload, &n.ReadAt); err != nil {
			return nil, err
		}
		result = append(result, &n)
	}
	if result == nil {
		result = []*tdomain.Notification{}
	}
	return result, rows.Err()
}

func (r *postgresNotifRepo) MarkRead(ctx context.Context, id, userID uuid.UUID) error {
	tag, err := r.pool.Exec(ctx,
		`UPDATE user_notifications SET read_at = NOW() WHERE id = $1 AND user_id = $2`,
		id, userID)
	if err != nil {
		return err
	}
	if tag.RowsAffected() == 0 {
		return tdomain.ErrNotificationNotFound
	}
	return nil
}

// ---- compile-time interface assertions ----
var _ tdomain.UsageRepository = (*postgresUsageRepo)(nil)
var _ tdomain.SessionRepository = (*postgresSessionRepo)(nil)
var _ tdomain.NotificationRepository = (*postgresNotifRepo)(nil)

// suppress unused import warning for strings
var _ = strings.TrimSpace
