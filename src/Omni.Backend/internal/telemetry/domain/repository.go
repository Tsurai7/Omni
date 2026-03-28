package domain

import (
	"context"

	"github.com/google/uuid"
)

// UsageRepository is the persistence port for usage records.
type UsageRepository interface {
	// BulkInsert inserts multiple records in a single transaction.
	BulkInsert(ctx context.Context, records []*UsageRecord) error

	// List returns aggregated usage grouped by period/app/category.
	List(ctx context.Context, q UsageQuery) ([]*UsageAggregate, error)
}

// UsageQuery carries filter parameters for usage listing.
type UsageQuery struct {
	UserID           uuid.UUID
	From             string // YYYY-MM-DD
	To               string // YYYY-MM-DD
	GroupBy          string // day | week | month
	CategoryFilter   string
	AppFilter        string
	UTCOffsetMinutes int
}

// UsageAggregate is one row in the grouped usage response.
type UsageAggregate struct {
	Period       string
	AppName      string
	Category     string
	TotalSeconds int64
}

// SessionRepository is the persistence port for sessions.
type SessionRepository interface {
	// BulkInsert inserts multiple sessions in a single transaction.
	BulkInsert(ctx context.Context, sessions []*Session) error

	// List returns sessions for a user within a date range.
	List(ctx context.Context, q SessionQuery) ([]*Session, error)
}

// SessionQuery carries filter parameters for session listing.
type SessionQuery struct {
	UserID           uuid.UUID
	From             string
	To               string
	UTCOffsetMinutes int
}

// NotificationRepository is the persistence port for notifications.
type NotificationRepository interface {
	// List returns up to 20 recent notifications for a user.
	List(ctx context.Context, userID uuid.UUID, unreadOnly bool) ([]*Notification, error)

	// MarkRead marks a notification as read. Returns ErrNotificationNotFound
	// when the notification does not exist or belongs to another user.
	MarkRead(ctx context.Context, id, userID uuid.UUID) error
}
