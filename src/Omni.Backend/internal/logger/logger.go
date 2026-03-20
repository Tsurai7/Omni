package logger

import (
	"log/slog"
	"os"
)

// New returns a structured logger. Uses JSON format (production-friendly).
// If debug is true, DEBUG-level messages are included; otherwise INFO and above.
func New(debug bool) *slog.Logger {
	level := slog.LevelInfo
	if debug {
		level = slog.LevelDebug
	}

	handler := slog.NewJSONHandler(os.Stdout, &slog.HandlerOptions{
		Level: level,
	})

	return slog.New(handler)
}
