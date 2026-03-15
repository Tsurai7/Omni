package telemetry

import (
	"testing"
	"time"

	"github.com/google/uuid"
)

func TestNewKafkaPublisher_EmptyBrokers_NoOp(t *testing.T) {
	pub, err := NewKafkaPublisher("", "omni.telemetry.events")
	if err != nil {
		t.Fatalf("NewKafkaPublisher: %v", err)
	}
	if pub.writer != nil {
		t.Error("expected nil writer when brokers empty")
	}
	// PublishUsage and PublishSession should not panic when writer is nil
	pub.PublishUsage(uuid.MustParse("550e8400-e29b-41d4-a716-446655440000"), "App", "Work", 60, time.Now().UTC())
	pub.PublishSession(uuid.MustParse("550e8400-e29b-41d4-a716-446655440000"), "Focus", "work", time.Now().UTC(), 120)
	if err := pub.Close(); err != nil {
		t.Errorf("Close: %v", err)
	}
}
