// Package domain contains pure business types for the telemetry domain.
package domain

import (
	"time"

	"github.com/google/uuid"
)

// UsageRecord represents app-usage time for a user.
type UsageRecord struct {
	ID              uuid.UUID
	UserID          uuid.UUID
	AppName         string
	Category        string
	DurationSeconds int64
	RecordedAt      time.Time
}

// Session represents a focused work session.
type Session struct {
	ID              uuid.UUID
	UserID          uuid.UUID
	Name            string
	ActivityType    string
	StartedAt       time.Time
	DurationSeconds int64
}

// Notification is a productivity recommendation surfaced to the user.
type Notification struct {
	ID            uuid.UUID
	UserID        uuid.UUID
	Type          string
	Title         string
	Body          string
	ActionType    string
	ActionPayload []byte
	CreatedAt     time.Time
	ReadAt        *time.Time
}
