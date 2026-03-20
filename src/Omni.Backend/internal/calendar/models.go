package calendar

import "time"

// GoogleToken stores a user's Google OAuth tokens.
type GoogleToken struct {
	UserID       string    `json:"user_id"`
	AccessToken  string    `json:"access_token"`
	RefreshToken string    `json:"refresh_token"`
	ExpiresAt    time.Time `json:"expires_at"`
	Email        string    `json:"email"`
	ConnectedAt  time.Time `json:"connected_at"`
}

// CalendarEvent is a single event returned from GET /calendar/events.
type CalendarEvent struct {
	ID               string     `json:"id"`
	UserID           string     `json:"user_id"`
	GoogleEventID    *string    `json:"google_event_id,omitempty"`
	Title            string     `json:"title"`
	Description      *string    `json:"description,omitempty"`
	StartAt          time.Time  `json:"start_at"`
	EndAt            *time.Time `json:"end_at,omitempty"`
	IsAllDay         bool       `json:"is_all_day"`
	GoogleCalendarID *string    `json:"google_calendar_id,omitempty"`
	Color            *string    `json:"color,omitempty"`
	OmniTaskID       *string    `json:"omni_task_id,omitempty"`
	Source           string     `json:"source"` // "omni_task" | "google_calendar"
	Priority         *string    `json:"priority,omitempty"`
	TaskStatus       *string    `json:"task_status,omitempty"`
	LastSyncedAt     time.Time  `json:"last_synced_at"`
}

// EventsResponse is the response for GET /calendar/events.
type EventsResponse struct {
	Events []CalendarEvent `json:"events"`
}

// StatusResponse is the response for GET /calendar/status.
type StatusResponse struct {
	Connected    bool    `json:"connected"`
	Email        *string `json:"email,omitempty"`
	LastSyncedAt *string `json:"last_synced_at,omitempty"`
}

// AuthURLResponse is the response for GET /calendar/auth/google.
type AuthURLResponse struct {
	URL string `json:"url"`
}

// ConnectRequest is the body for POST /calendar/auth/google/connect.
type ConnectRequest struct {
	Code string `json:"code"`
}

// CreateEventRequest is the body for POST /calendar/events.
type CreateEventRequest struct {
	Title       string  `json:"title"`
	Description *string `json:"description,omitempty"`
	StartAt     string  `json:"start_at"`
	EndAt       *string `json:"end_at,omitempty"`
	IsAllDay    bool    `json:"is_all_day"`
}

// CreateEventResponse is the response for POST /calendar/events.
type CreateEventResponse struct {
	GoogleEventID string `json:"google_event_id"`
}
