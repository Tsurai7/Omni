package domain

import "errors"

var (
	ErrNotificationNotFound = errors.New("notification not found")
	ErrNegativeDuration     = errors.New("duration_seconds must be positive")
	ErrAppNameEmpty         = errors.New("app_name cannot be empty")
	ErrSessionNameEmpty     = errors.New("session name cannot be empty")
	ErrInvalidStartedAt     = errors.New("started_at must be a valid RFC3339 timestamp")
)
