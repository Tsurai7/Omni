package service_test

import (
	"context"
	"errors"
	"log/slog"
	"testing"
	"time"

	tdomain "omni-backend/internal/telemetry/domain"
	"omni-backend/internal/telemetry/service"

	"github.com/google/uuid"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/mock"
	"github.com/stretchr/testify/require"
)

// ---- mocks ----

type mockUsageRepo struct{ mock.Mock }

func (m *mockUsageRepo) BulkInsert(ctx context.Context, records []*tdomain.UsageRecord) error {
	return m.Called(ctx, records).Error(0)
}

func (m *mockUsageRepo) List(ctx context.Context, q tdomain.UsageQuery) ([]*tdomain.UsageAggregate, error) {
	args := m.Called(ctx, q)
	if args.Get(0) == nil {
		return nil, args.Error(1)
	}
	return args.Get(0).([]*tdomain.UsageAggregate), args.Error(1)
}

type mockSessionRepo struct{ mock.Mock }

func (m *mockSessionRepo) BulkInsert(ctx context.Context, sessions []*tdomain.Session) error {
	return m.Called(ctx, sessions).Error(0)
}

func (m *mockSessionRepo) List(ctx context.Context, q tdomain.SessionQuery) ([]*tdomain.Session, error) {
	args := m.Called(ctx, q)
	if args.Get(0) == nil {
		return nil, args.Error(1)
	}
	return args.Get(0).([]*tdomain.Session), args.Error(1)
}

type mockNotifRepo struct{ mock.Mock }

func (m *mockNotifRepo) List(ctx context.Context, userID uuid.UUID, unreadOnly bool) ([]*tdomain.Notification, error) {
	args := m.Called(ctx, userID, unreadOnly)
	if args.Get(0) == nil {
		return nil, args.Error(1)
	}
	return args.Get(0).([]*tdomain.Notification), args.Error(1)
}

func (m *mockNotifRepo) MarkRead(ctx context.Context, id, userID uuid.UUID) error {
	return m.Called(ctx, id, userID).Error(0)
}

type mockPublisher struct{ mock.Mock }

func (m *mockPublisher) PublishUsage(userID uuid.UUID, appName, category string, duration int64, recordedAt time.Time) {
	m.Called(userID, appName, category, duration, recordedAt)
}

func (m *mockPublisher) PublishSession(userID uuid.UUID, name, activityType string, startedAt time.Time, duration int64) {
	m.Called(userID, name, activityType, startedAt, duration)
}

func (m *mockPublisher) Close() error { return m.Called().Error(0) }

// ---- helpers ----

var (
	fixedTime = time.Date(2025, 1, 15, 12, 0, 0, 0, time.UTC)
	testUser  = uuid.MustParse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
)

func newSvc(u *mockUsageRepo, s *mockSessionRepo, n *mockNotifRepo, p *mockPublisher) service.TelemetryService {
	return service.NewWithClock(u, s, n, p, slog.Default(), func() time.Time { return fixedTime })
}

// ---- SyncUsage tests ----

func TestSyncUsage(t *testing.T) {
	tests := []struct {
		name      string
		cmd       service.SyncUsageCmd
		setupMock func(*mockUsageRepo, *mockPublisher)
		wantErr   error
		checkCall func(*testing.T, *mockUsageRepo, *mockPublisher)
	}{
		{
			name: "happy path — valid entries inserted and published",
			cmd: service.SyncUsageCmd{
				UserID: testUser,
				Entries: []service.UsageEntry{
					{AppName: "VSCode", Category: "Coding", DurationSeconds: 3600},
					{AppName: "Chrome", Category: "Research", DurationSeconds: 600},
				},
			},
			setupMock: func(u *mockUsageRepo, p *mockPublisher) {
				u.On("BulkInsert", mock.Anything, mock.MatchedBy(func(recs []*tdomain.UsageRecord) bool {
					return len(recs) == 2
				})).Return(nil)
				p.On("PublishUsage", testUser, "VSCode", "Coding", int64(3600), fixedTime).Return()
				p.On("PublishUsage", testUser, "Chrome", "Research", int64(600), fixedTime).Return()
			},
		},
		{
			name: "happy path — empty app_name entries silently skipped",
			cmd: service.SyncUsageCmd{
				UserID: testUser,
				Entries: []service.UsageEntry{
					{AppName: "", Category: "Coding", DurationSeconds: 100},
					{AppName: "VSCode", Category: "Coding", DurationSeconds: 200},
				},
			},
			setupMock: func(u *mockUsageRepo, p *mockPublisher) {
				u.On("BulkInsert", mock.Anything, mock.MatchedBy(func(recs []*tdomain.UsageRecord) bool {
					return len(recs) == 1 && recs[0].AppName == "VSCode"
				})).Return(nil)
				p.On("PublishUsage", mock.Anything, mock.Anything, mock.Anything, mock.Anything, mock.Anything).Return()
			},
		},
		{
			name: "corner case — zero or negative duration entries skipped",
			cmd: service.SyncUsageCmd{
				UserID: testUser,
				Entries: []service.UsageEntry{
					{AppName: "App", Category: "Cat", DurationSeconds: 0},
					{AppName: "App2", Category: "Cat", DurationSeconds: -1},
				},
			},
			// No BulkInsert call expected since all entries are filtered.
		},
		{
			name: "corner case — missing category defaults to 'Other'",
			cmd: service.SyncUsageCmd{
				UserID: testUser,
				Entries: []service.UsageEntry{
					{AppName: "Notes", Category: "", DurationSeconds: 120},
				},
			},
			setupMock: func(u *mockUsageRepo, p *mockPublisher) {
				u.On("BulkInsert", mock.Anything, mock.MatchedBy(func(recs []*tdomain.UsageRecord) bool {
					return len(recs) == 1 && recs[0].Category == "Other"
				})).Return(nil)
				p.On("PublishUsage", mock.Anything, mock.Anything, "Other", mock.Anything, mock.Anything).Return()
			},
		},
		{
			name: "corner case — repository error propagated",
			cmd: service.SyncUsageCmd{
				UserID:  testUser,
				Entries: []service.UsageEntry{{AppName: "App", DurationSeconds: 100}},
			},
			setupMock: func(u *mockUsageRepo, _ *mockPublisher) {
				u.On("BulkInsert", mock.Anything, mock.Anything).Return(errors.New("db down"))
			},
			wantErr: errors.New("db down"),
		},
		{
			name: "corner case — completely empty entries slice is a no-op",
			cmd:  service.SyncUsageCmd{UserID: testUser, Entries: []service.UsageEntry{}},
			// No BulkInsert call at all.
		},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			u := &mockUsageRepo{}
			s := &mockSessionRepo{}
			n := &mockNotifRepo{}
			p := &mockPublisher{}

			if tt.setupMock != nil {
				tt.setupMock(u, p)
			}

			err := newSvc(u, s, n, p).SyncUsage(context.Background(), tt.cmd)

			if tt.wantErr != nil {
				require.Error(t, err)
				return
			}
			require.NoError(t, err)
			u.AssertExpectations(t)
			p.AssertExpectations(t)
		})
	}
}

// ---- SyncSessions tests ----

func TestSyncSessions(t *testing.T) {
	validStartedAt := fixedTime.Format(time.RFC3339)

	tests := []struct {
		name      string
		cmd       service.SyncSessionsCmd
		setupMock func(*mockSessionRepo, *mockPublisher)
		wantErr   error
	}{
		{
			name: "happy path — valid sessions inserted and published",
			cmd: service.SyncSessionsCmd{
				UserID: testUser,
				Entries: []service.SessionEntry{
					{Name: "Deep Work", ActivityType: "focus", StartedAt: validStartedAt, DurationSeconds: 3600},
				},
			},
			setupMock: func(s *mockSessionRepo, p *mockPublisher) {
				s.On("BulkInsert", mock.Anything, mock.MatchedBy(func(sessions []*tdomain.Session) bool {
					return len(sessions) == 1 && sessions[0].Name == "Deep Work"
				})).Return(nil)
				p.On("PublishSession", testUser, "Deep Work", "focus", mock.Anything, int64(3600)).Return()
			},
		},
		{
			name: "corner case — missing activity_type defaults to 'other'",
			cmd: service.SyncSessionsCmd{
				UserID: testUser,
				Entries: []service.SessionEntry{
					{Name: "Meeting", ActivityType: "", StartedAt: validStartedAt, DurationSeconds: 1800},
				},
			},
			setupMock: func(s *mockSessionRepo, p *mockPublisher) {
				s.On("BulkInsert", mock.Anything, mock.MatchedBy(func(sessions []*tdomain.Session) bool {
					return len(sessions) == 1 && sessions[0].ActivityType == "other"
				})).Return(nil)
				p.On("PublishSession", mock.Anything, mock.Anything, "other", mock.Anything, mock.Anything).Return()
			},
		},
		{
			name: "corner case — invalid started_at returns ErrInvalidStartedAt",
			cmd: service.SyncSessionsCmd{
				UserID: testUser,
				Entries: []service.SessionEntry{
					{Name: "Session", StartedAt: "not-a-date", DurationSeconds: 100},
				},
			},
			wantErr: tdomain.ErrInvalidStartedAt,
		},
		{
			name: "corner case — empty name entry silently skipped",
			cmd: service.SyncSessionsCmd{
				UserID: testUser,
				Entries: []service.SessionEntry{
					{Name: "", StartedAt: validStartedAt, DurationSeconds: 100},
					{Name: "Valid", StartedAt: validStartedAt, DurationSeconds: 200},
				},
			},
			setupMock: func(s *mockSessionRepo, p *mockPublisher) {
				s.On("BulkInsert", mock.Anything, mock.MatchedBy(func(sessions []*tdomain.Session) bool {
					return len(sessions) == 1 && sessions[0].Name == "Valid"
				})).Return(nil)
				p.On("PublishSession", mock.Anything, mock.Anything, mock.Anything, mock.Anything, mock.Anything).Return()
			},
		},
		{
			name: "corner case — zero duration silently skipped",
			cmd: service.SyncSessionsCmd{
				UserID: testUser,
				Entries: []service.SessionEntry{
					{Name: "Ghost", StartedAt: validStartedAt, DurationSeconds: 0},
				},
			},
			// No BulkInsert expected.
		},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			u := &mockUsageRepo{}
			s := &mockSessionRepo{}
			n := &mockNotifRepo{}
			p := &mockPublisher{}

			if tt.setupMock != nil {
				tt.setupMock(s, p)
			}

			err := newSvc(u, s, n, p).SyncSessions(context.Background(), tt.cmd)

			if tt.wantErr != nil {
				require.Error(t, err)
				assert.ErrorIs(t, err, tt.wantErr)
				return
			}
			require.NoError(t, err)
			s.AssertExpectations(t)
			p.AssertExpectations(t)
		})
	}
}

// ---- MarkNotificationRead tests ----

func TestMarkNotificationRead(t *testing.T) {
	notifID := uuid.New()

	tests := []struct {
		name      string
		notifID   uuid.UUID
		userID    uuid.UUID
		setupMock func(*mockNotifRepo)
		wantErr   error
	}{
		{
			name:    "happy path — marked read",
			notifID: notifID, userID: testUser,
			setupMock: func(n *mockNotifRepo) {
				n.On("MarkRead", mock.Anything, notifID, testUser).Return(nil)
			},
		},
		{
			name:    "corner case — not found propagated",
			notifID: notifID, userID: testUser,
			setupMock: func(n *mockNotifRepo) {
				n.On("MarkRead", mock.Anything, notifID, testUser).Return(tdomain.ErrNotificationNotFound)
			},
			wantErr: tdomain.ErrNotificationNotFound,
		},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			u := &mockUsageRepo{}
			s := &mockSessionRepo{}
			n := &mockNotifRepo{}
			p := &mockPublisher{}

			if tt.setupMock != nil {
				tt.setupMock(n)
			}

			err := newSvc(u, s, n, p).MarkNotificationRead(context.Background(), tt.notifID, tt.userID)

			if tt.wantErr != nil {
				assert.ErrorIs(t, err, tt.wantErr)
				return
			}
			require.NoError(t, err)
			n.AssertExpectations(t)
		})
	}
}
