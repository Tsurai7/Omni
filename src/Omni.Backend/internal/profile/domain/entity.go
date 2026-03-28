// Package domain contains pure business types for the profile/auth domain.
package domain

import (
	"time"

	"github.com/google/uuid"
)

// User is the authentication aggregate.
type User struct {
	ID           uuid.UUID
	Email        string
	PasswordHash string
	CreatedAt    time.Time
}
