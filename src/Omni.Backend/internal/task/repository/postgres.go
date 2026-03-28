// Package repository implements the TaskRepository interface against PostgreSQL.
package repository

import (
	"context"
	"errors"

	"omni-backend/internal/task/domain"

	"github.com/google/uuid"
	"github.com/jackc/pgx/v5"
	"github.com/jackc/pgx/v5/pgxpool"
)

type postgresTaskRepo struct {
	pool *pgxpool.Pool
}

// NewPostgres returns a TaskRepository backed by PostgreSQL.
func NewPostgres(pool *pgxpool.Pool) domain.TaskRepository {
	return &postgresTaskRepo{pool: pool}
}

func (r *postgresTaskRepo) GetByID(ctx context.Context, id, userID uuid.UUID) (*domain.Task, error) {
	const q = `
		SELECT id, user_id, title, status, priority,
		       due_date, scheduled_for, google_event_id,
		       created_at, updated_at
		FROM tasks
		WHERE id = $1 AND user_id = $2`

	row := r.pool.QueryRow(ctx, q, id, userID)
	task, err := scanTask(row)
	if err != nil {
		if errors.Is(err, pgx.ErrNoRows) {
			return nil, domain.ErrTaskNotFound
		}
		return nil, err
	}
	return task, nil
}

func (r *postgresTaskRepo) ListByUser(ctx context.Context, userID uuid.UUID) ([]*domain.Task, error) {
	const q = `
		SELECT id, user_id, title, status, priority,
		       due_date, scheduled_for, google_event_id,
		       created_at, updated_at
		FROM tasks
		WHERE user_id = $1
		ORDER BY created_at DESC`

	rows, err := r.pool.Query(ctx, q, userID)
	if err != nil {
		return nil, err
	}
	defer rows.Close()

	var tasks []*domain.Task
	for rows.Next() {
		t, err := scanTask(rows)
		if err != nil {
			return nil, err
		}
		tasks = append(tasks, t)
	}
	if err := rows.Err(); err != nil {
		return nil, err
	}
	if tasks == nil {
		tasks = []*domain.Task{}
	}
	return tasks, nil
}

func (r *postgresTaskRepo) Create(ctx context.Context, task *domain.Task) error {
	const q = `
		INSERT INTO tasks
		    (id, user_id, title, status, priority, due_date, scheduled_for, created_at, updated_at)
		VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9)`

	_, err := r.pool.Exec(ctx, q,
		task.ID, task.UserID, task.Title, task.Status, task.Priority,
		task.DueDate, task.ScheduledFor, task.CreatedAt, task.UpdatedAt,
	)
	return err
}

func (r *postgresTaskRepo) Update(ctx context.Context, task *domain.Task) error {
	const q = `
		UPDATE tasks
		SET title        = $1,
		    status       = $2,
		    priority     = $3,
		    due_date     = $4,
		    scheduled_for = $5,
		    updated_at   = $6
		WHERE id = $7 AND user_id = $8`

	tag, err := r.pool.Exec(ctx, q,
		task.Title, task.Status, task.Priority,
		task.DueDate, task.ScheduledFor, task.UpdatedAt,
		task.ID, task.UserID,
	)
	if err != nil {
		return err
	}
	if tag.RowsAffected() == 0 {
		return domain.ErrTaskNotFound
	}
	return nil
}

func (r *postgresTaskRepo) Delete(ctx context.Context, id, userID uuid.UUID) error {
	const q = `DELETE FROM tasks WHERE id = $1 AND user_id = $2`

	tag, err := r.pool.Exec(ctx, q, id, userID)
	if err != nil {
		return err
	}
	if tag.RowsAffected() == 0 {
		return domain.ErrTaskNotFound
	}
	return nil
}

// scanTask reads one task row from a pgx.Row or pgx.Rows.
type scanner interface {
	Scan(dest ...any) error
}

func scanTask(row scanner) (*domain.Task, error) {
	var t domain.Task
	err := row.Scan(
		&t.ID, &t.UserID, &t.Title, &t.Status, &t.Priority,
		&t.DueDate, &t.ScheduledFor, &t.GoogleEventID,
		&t.CreatedAt, &t.UpdatedAt,
	)
	if err != nil {
		return nil, err
	}
	return &t, nil
}
