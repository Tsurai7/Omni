// Package repository implements the UserRepository interface against PostgreSQL.
package repository

import (
	"context"
	"errors"

	"omni-backend/internal/profile/domain"

	"github.com/google/uuid"
	"github.com/jackc/pgx/v5"
	"github.com/jackc/pgx/v5/pgconn"
	"github.com/jackc/pgx/v5/pgxpool"
)

type postgresUserRepo struct {
	pool *pgxpool.Pool
}

// NewPostgres returns a UserRepository backed by PostgreSQL.
func NewPostgres(pool *pgxpool.Pool) domain.UserRepository {
	return &postgresUserRepo{pool: pool}
}

func (r *postgresUserRepo) GetByEmail(ctx context.Context, email string) (*domain.User, error) {
	const q = `SELECT id, email, password_hash, created_at FROM users WHERE email = $1`
	row := r.pool.QueryRow(ctx, q, email)
	return scanUser(row)
}

func (r *postgresUserRepo) GetByID(ctx context.Context, id uuid.UUID) (*domain.User, error) {
	const q = `SELECT id, email, password_hash, created_at FROM users WHERE id = $1`
	row := r.pool.QueryRow(ctx, q, id)
	return scanUser(row)
}

func (r *postgresUserRepo) Create(ctx context.Context, user *domain.User) error {
	const q = `INSERT INTO users (id, email, password_hash, created_at) VALUES ($1, $2, $3, $4)`
	_, err := r.pool.Exec(ctx, q, user.ID, user.Email, user.PasswordHash, user.CreatedAt)
	if err != nil {
		if isUniqueViolation(err) {
			return domain.ErrEmailAlreadyTaken
		}
		return err
	}
	return nil
}

func scanUser(row pgx.Row) (*domain.User, error) {
	var u domain.User
	err := row.Scan(&u.ID, &u.Email, &u.PasswordHash, &u.CreatedAt)
	if err != nil {
		if errors.Is(err, pgx.ErrNoRows) {
			return nil, domain.ErrUserNotFound
		}
		return nil, err
	}
	return &u, nil
}

func isUniqueViolation(err error) bool {
	var pgErr *pgconn.PgError
	return errors.As(err, &pgErr) && pgErr.Code == "23505"
}
