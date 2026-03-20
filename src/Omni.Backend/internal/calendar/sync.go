package calendar

import (
	"context"
	"log/slog"
	"time"

	"github.com/jackc/pgx/v5/pgxpool"
)

// Syncer handles two-way Google Calendar synchronization for a user.
type Syncer struct {
	pool   *pgxpool.Pool
	google *GoogleClient
	logger *slog.Logger
}

func NewSyncer(pool *pgxpool.Pool, google *GoogleClient, logger *slog.Logger) *Syncer {
	return &Syncer{pool: pool, google: google, logger: logger}
}

// SyncForUser performs a full two-way sync for the given user.
// It pulls GCal events into calendar_events and pushes Omni tasks with due_date to GCal.
func (s *Syncer) SyncForUser(ctx context.Context, userID string) error {
	token, err := s.getValidToken(ctx, userID)
	if err != nil {
		return err
	}

	if err := s.pullFromGoogle(ctx, userID, token); err != nil {
		s.logger.Warn("calendar pull failed", "user_id", userID, "error", err)
	}

	if err := s.pushTasksToGoogle(ctx, userID, token); err != nil {
		s.logger.Warn("calendar push failed", "user_id", userID, "error", err)
	}

	return nil
}

// getValidToken returns a valid access token, refreshing if needed.
func (s *Syncer) getValidToken(ctx context.Context, userID string) (string, error) {
	var accessToken, refreshToken string
	var expiresAt time.Time

	err := s.pool.QueryRow(ctx,
		`SELECT access_token, refresh_token, expires_at FROM user_google_tokens WHERE user_id = $1`,
		userID).Scan(&accessToken, &refreshToken, &expiresAt)
	if err != nil {
		return "", err
	}

	// Refresh if expiring within 5 minutes
	if time.Until(expiresAt) < 5*time.Minute {
		tr, err := s.google.RefreshAccessToken(ctx, refreshToken)
		if err != nil {
			return "", err
		}
		accessToken = tr.AccessToken
		newExpiry := time.Now().Add(time.Duration(tr.ExpiresIn) * time.Second)
		_, err = s.pool.Exec(ctx,
			`UPDATE user_google_tokens SET access_token = $1, expires_at = $2 WHERE user_id = $3`,
			accessToken, newExpiry, userID)
		if err != nil {
			s.logger.Warn("failed to persist refreshed token", "user_id", userID, "error", err)
		}
	}

	return accessToken, nil
}

// pullFromGoogle fetches Google Calendar events and upserts them into calendar_events.
func (s *Syncer) pullFromGoogle(ctx context.Context, userID, accessToken string) error {
	timeMin := time.Now().AddDate(0, 0, -30)
	timeMax := time.Now().AddDate(0, 3, 0)

	events, err := s.google.ListEvents(ctx, accessToken, timeMin, timeMax)
	if err != nil {
		return err
	}

	for _, evt := range events {
		if evt.Status == "cancelled" {
			_, _ = s.pool.Exec(ctx,
				`DELETE FROM calendar_events WHERE user_id = $1 AND google_event_id = $2`,
				userID, evt.ID)
			continue
		}

		var startAt time.Time
		isAllDay := false
		if evt.Start.Date != "" {
			startAt, err = time.Parse("2006-01-02", evt.Start.Date)
			isAllDay = true
		} else {
			startAt, err = time.Parse(time.RFC3339, evt.Start.DateTime)
		}
		if err != nil {
			continue
		}

		var endAt *time.Time
		if evt.End.DateTime != "" {
			t, err := time.Parse(time.RFC3339, evt.End.DateTime)
			if err == nil {
				endAt = &t
			}
		} else if evt.End.Date != "" {
			t, err := time.Parse("2006-01-02", evt.End.Date)
			if err == nil {
				endAt = &t
			}
		}

		var color *string
		if evt.ColorID != "" {
			color = &evt.ColorID
		}
		var desc *string
		if evt.Description != "" {
			desc = &evt.Description
		}
		calendarID := "primary"

		_, err = s.pool.Exec(ctx,
			`INSERT INTO calendar_events
			   (user_id, google_event_id, title, description, start_at, end_at, is_all_day,
			    google_calendar_id, color, last_synced_at)
			 VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,NOW())
			 ON CONFLICT (user_id, google_event_id) DO UPDATE
			   SET title = EXCLUDED.title,
			       description = EXCLUDED.description,
			       start_at = EXCLUDED.start_at,
			       end_at = EXCLUDED.end_at,
			       is_all_day = EXCLUDED.is_all_day,
			       color = EXCLUDED.color,
			       last_synced_at = NOW()`,
			userID, evt.ID, evt.Summary, desc, startAt, endAt, isAllDay,
			calendarID, color)
		if err != nil {
			s.logger.Warn("failed to upsert calendar event", "event_id", evt.ID, "error", err)
		}
	}

	return nil
}

// pushTasksToGoogle creates or updates Google Calendar events for Omni tasks with a due_date.
func (s *Syncer) pushTasksToGoogle(ctx context.Context, userID, accessToken string) error {
	rows, err := s.pool.Query(ctx,
		`SELECT id::text, title, due_date, google_event_id
		 FROM tasks
		 WHERE user_id = $1 AND due_date IS NOT NULL AND status != 'cancelled'`,
		userID)
	if err != nil {
		return err
	}
	defer rows.Close()

	type taskRow struct {
		ID            string
		Title         string
		DueDate       time.Time
		GoogleEventID *string
	}

	var tasks []taskRow
	for rows.Next() {
		var t taskRow
		if err := rows.Scan(&t.ID, &t.Title, &t.DueDate, &t.GoogleEventID); err != nil {
			continue
		}
		tasks = append(tasks, t)
	}
	rows.Close()

	for _, t := range tasks {
		if t.GoogleEventID != nil && *t.GoogleEventID != "" {
			// Update existing GCal event
			err = s.google.UpdateEvent(ctx, accessToken, *t.GoogleEventID,
				t.Title, "Omni task", t.DueDate, true)
			if err != nil {
				s.logger.Warn("failed to update gcal event", "task_id", t.ID, "error", err)
			}
		} else {
			// Create new GCal event
			evt, err := s.google.CreateEvent(ctx, accessToken, t.Title, "Omni task", t.DueDate, nil, true)
			if err != nil {
				s.logger.Warn("failed to create gcal event", "task_id", t.ID, "error", err)
				continue
			}
			// Store the google_event_id on the task
			_, err = s.pool.Exec(ctx,
				`UPDATE tasks SET google_event_id = $1 WHERE id = $2`,
				evt.ID, t.ID)
			if err != nil {
				s.logger.Warn("failed to store google_event_id on task", "task_id", t.ID, "error", err)
			}
		}
	}

	return nil
}

// DeleteTaskEventFromGoogle removes the GCal event linked to a task (called when task is deleted).
func (s *Syncer) DeleteTaskEventFromGoogle(ctx context.Context, userID, taskID string) {
	token, err := s.getValidToken(ctx, userID)
	if err != nil {
		return
	}

	var googleEventID *string
	err = s.pool.QueryRow(ctx,
		`SELECT google_event_id FROM tasks WHERE id = $1 AND user_id = $2`,
		taskID, userID).Scan(&googleEventID)
	if err != nil || googleEventID == nil || *googleEventID == "" {
		return
	}

	_ = s.google.DeleteEvent(ctx, token, *googleEventID)
}
