package domain

import (
	"context"

	"github.com/google/uuid"
)

// UserRepository is the persistence port for the profile domain.
type UserRepository interface {
	// GetByEmail returns a user by email. Returns ErrUserNotFound when missing.
	GetByEmail(ctx context.Context, email string) (*User, error)

	// GetByID returns a user by primary key. Returns ErrUserNotFound when missing.
	GetByID(ctx context.Context, id uuid.UUID) (*User, error)

	// Create persists a new user. Returns ErrEmailAlreadyTaken on duplicate email.
	Create(ctx context.Context, user *User) error
}
