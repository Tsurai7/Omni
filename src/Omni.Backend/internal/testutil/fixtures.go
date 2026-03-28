package testutil

import (
	"time"

	"omni-backend/internal/profile/domain"
	taskdomain "omni-backend/internal/task/domain"

	"github.com/google/uuid"
)

// ---- Task fixtures ----

// TaskFixture returns a domain.Task with sensible defaults.
// Override specific fields using functional options.
func TaskFixture(opts ...func(*taskdomain.Task)) *taskdomain.Task {
	t := &taskdomain.Task{
		ID:        uuid.New(),
		UserID:    uuid.New(),
		Title:     "Test Task",
		Status:    taskdomain.StatusPending,
		Priority:  taskdomain.PriorityMedium,
		CreatedAt: time.Date(2025, 1, 15, 10, 0, 0, 0, time.UTC),
		UpdatedAt: time.Date(2025, 1, 15, 10, 0, 0, 0, time.UTC),
	}
	for _, o := range opts {
		o(t)
	}
	return t
}

func WithTaskUserID(id uuid.UUID) func(*taskdomain.Task) {
	return func(t *taskdomain.Task) { t.UserID = id }
}

func WithTaskStatus(s taskdomain.TaskStatus) func(*taskdomain.Task) {
	return func(t *taskdomain.Task) { t.Status = s }
}

func WithTaskPriority(p taskdomain.TaskPriority) func(*taskdomain.Task) {
	return func(t *taskdomain.Task) { t.Priority = p }
}

func WithTaskTitle(title string) func(*taskdomain.Task) {
	return func(t *taskdomain.Task) { t.Title = title }
}

func WithTaskDueDate(d time.Time) func(*taskdomain.Task) {
	return func(t *taskdomain.Task) { t.DueDate = &d }
}

// ---- User fixtures ----

// UserFixture returns a domain.User with sensible defaults.
func UserFixture(opts ...func(*domain.User)) *domain.User {
	u := &domain.User{
		ID:           uuid.New(),
		Email:        "test+" + uuid.New().String()[:8] + "@example.com",
		PasswordHash: "$2a$10$testhashtesthashhashhas",
		CreatedAt:    time.Date(2025, 1, 15, 10, 0, 0, 0, time.UTC),
	}
	for _, o := range opts {
		o(u)
	}
	return u
}

func WithUserEmail(email string) func(*domain.User) {
	return func(u *domain.User) { u.Email = email }
}

func WithUserPasswordHash(hash string) func(*domain.User) {
	return func(u *domain.User) { u.PasswordHash = hash }
}

// Ptr returns a pointer to any value — useful in test setup.
func Ptr[T any](v T) *T { return &v }
