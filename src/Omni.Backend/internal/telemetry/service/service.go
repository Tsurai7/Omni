// Package service contains the telemetry business logic.
package service

import (
	"context"
	"log/slog"
	"strings"
	"time"

	"omni-backend/internal/telemetry"
	tdomain "omni-backend/internal/telemetry/domain"

	"github.com/google/uuid"
)

// TelemetryService is the application use-case interface for telemetry.
type TelemetryService interface {
	SyncUsage(ctx context.Context, cmd SyncUsageCmd) error
	ListUsage(ctx context.Context, q tdomain.UsageQuery) ([]*tdomain.UsageAggregate, error)
	SyncSessions(ctx context.Context, cmd SyncSessionsCmd) error
	ListSessions(ctx context.Context, q tdomain.SessionQuery) ([]*tdomain.Session, error)
	ListNotifications(ctx context.Context, userID uuid.UUID, unreadOnly bool) ([]*tdomain.Notification, error)
	MarkNotificationRead(ctx context.Context, notifID, userID uuid.UUID) error
}

// UsageEntry is one usage entry from the client sync payload.
type UsageEntry struct {
	AppName         string
	Category        string
	DurationSeconds int64
}

// SyncUsageCmd is the input value object for usage synchronisation.
type SyncUsageCmd struct {
	UserID  uuid.UUID
	Entries []UsageEntry
}

// SessionEntry is one session entry from the client sync payload.
type SessionEntry struct {
	Name            string
	ActivityType    string
	StartedAt       string // RFC3339
	DurationSeconds int64
}

// SyncSessionsCmd is the input value object for session synchronisation.
type SyncSessionsCmd struct {
	UserID  uuid.UUID
	Entries []SessionEntry
}

type telemetryService struct {
	usageRepo   tdomain.UsageRepository
	sessionRepo tdomain.SessionRepository
	notifRepo   tdomain.NotificationRepository
	publisher   telemetry.Publisher
	logger      *slog.Logger
	clock       func() time.Time
}

// New returns a TelemetryService wired to the given repositories.
func New(
	usageRepo tdomain.UsageRepository,
	sessionRepo tdomain.SessionRepository,
	notifRepo tdomain.NotificationRepository,
	publisher telemetry.Publisher,
	logger *slog.Logger,
) TelemetryService {
	return &telemetryService{
		usageRepo:   usageRepo,
		sessionRepo: sessionRepo,
		notifRepo:   notifRepo,
		publisher:   publisher,
		logger:      logger,
		clock:       time.Now,
	}
}

// NewWithClock returns a TelemetryService with an injectable clock.
func NewWithClock(
	usageRepo tdomain.UsageRepository,
	sessionRepo tdomain.SessionRepository,
	notifRepo tdomain.NotificationRepository,
	publisher telemetry.Publisher,
	logger *slog.Logger,
	clock func() time.Time,
) TelemetryService {
	return &telemetryService{
		usageRepo:   usageRepo,
		sessionRepo: sessionRepo,
		notifRepo:   notifRepo,
		publisher:   publisher,
		logger:      logger,
		clock:       clock,
	}
}

func (s *telemetryService) SyncUsage(ctx context.Context, cmd SyncUsageCmd) error {
	now := s.clock().UTC()
	var records []*tdomain.UsageRecord

	for _, e := range cmd.Entries {
		appName := strings.TrimSpace(e.AppName)
		if appName == "" {
			continue // skip silently — client may send partial data
		}
		if e.DurationSeconds <= 0 {
			continue
		}
		category := e.Category
		if category == "" {
			category = "Other"
		}
		records = append(records, &tdomain.UsageRecord{
			ID:              uuid.New(),
			UserID:          cmd.UserID,
			AppName:         appName,
			Category:        category,
			DurationSeconds: e.DurationSeconds,
			RecordedAt:      now,
		})
	}

	if len(records) == 0 {
		return nil
	}

	if err := s.usageRepo.BulkInsert(ctx, records); err != nil {
		s.logger.ErrorContext(ctx, "failed to bulk insert usage records", "user_id", cmd.UserID, "error", err)
		return err
	}

	// Best-effort publish — do not fail the request if Kafka is unavailable.
	if s.publisher != nil {
		for _, r := range records {
			s.publisher.PublishUsage(r.UserID, r.AppName, r.Category, r.DurationSeconds, r.RecordedAt)
		}
	}

	s.logger.InfoContext(ctx, "usage synced", "user_id", cmd.UserID, "count", len(records))
	return nil
}

func (s *telemetryService) ListUsage(ctx context.Context, q tdomain.UsageQuery) ([]*tdomain.UsageAggregate, error) {
	return s.usageRepo.List(ctx, q)
}

func (s *telemetryService) SyncSessions(ctx context.Context, cmd SyncSessionsCmd) error {
	var sessions []*tdomain.Session

	for _, e := range cmd.Entries {
		name := strings.TrimSpace(e.Name)
		if name == "" || e.DurationSeconds <= 0 {
			continue
		}

		startedAt, err := time.Parse(time.RFC3339, e.StartedAt)
		if err != nil {
			return tdomain.ErrInvalidStartedAt
		}

		activityType := e.ActivityType
		if activityType == "" {
			activityType = "other"
		}

		sessions = append(sessions, &tdomain.Session{
			ID:              uuid.New(),
			UserID:          cmd.UserID,
			Name:            name,
			ActivityType:    activityType,
			StartedAt:       startedAt.UTC(),
			DurationSeconds: e.DurationSeconds,
		})
	}

	if len(sessions) == 0 {
		return nil
	}

	if err := s.sessionRepo.BulkInsert(ctx, sessions); err != nil {
		s.logger.ErrorContext(ctx, "failed to bulk insert sessions", "user_id", cmd.UserID, "error", err)
		return err
	}

	// Best-effort publish.
	if s.publisher != nil {
		for _, sess := range sessions {
			s.publisher.PublishSession(sess.UserID, sess.Name, sess.ActivityType, sess.StartedAt, sess.DurationSeconds)
		}
	}

	s.logger.InfoContext(ctx, "sessions synced", "user_id", cmd.UserID, "count", len(sessions))
	return nil
}

func (s *telemetryService) ListSessions(ctx context.Context, q tdomain.SessionQuery) ([]*tdomain.Session, error) {
	return s.sessionRepo.List(ctx, q)
}

func (s *telemetryService) ListNotifications(ctx context.Context, userID uuid.UUID, unreadOnly bool) ([]*tdomain.Notification, error) {
	return s.notifRepo.List(ctx, userID, unreadOnly)
}

func (s *telemetryService) MarkNotificationRead(ctx context.Context, notifID, userID uuid.UUID) error {
	return s.notifRepo.MarkRead(ctx, notifID, userID)
}
