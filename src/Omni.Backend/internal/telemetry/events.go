package telemetry

import (
	"time"

	"github.com/google/uuid"
)

// TelemetryEvent is the JSON payload sent to Redpanda (omni.telemetry.events).
type TelemetryEvent struct {
	EventID         string    `json:"event_id"`
	EventType       string    `json:"event_type"` // "usage" | "session"
	UserID          string    `json:"user_id"`
	RecordedAt      time.Time `json:"recorded_at,omitempty"` // for usage
	StartedAt       time.Time `json:"started_at,omitempty"`  // for session
	AppName         string    `json:"app_name,omitempty"`
	Category        string    `json:"category,omitempty"`
	Name            string    `json:"name,omitempty"`
	ActivityType    string    `json:"activity_type,omitempty"`
	DurationSeconds int64     `json:"duration_seconds"`
}

// Publisher publishes telemetry events to Redpanda (non-blocking).
type Publisher interface {
	PublishUsage(userID uuid.UUID, appName, category string, durationSeconds int64, recordedAt time.Time)
	PublishSession(userID uuid.UUID, name, activityType string, startedAt time.Time, durationSeconds int64)
	Close() error
}
