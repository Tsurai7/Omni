package telemetry

import (
	"context"
	"encoding/json"
	"log"
	"strings"
	"time"

	"github.com/google/uuid"
	"github.com/segmentio/kafka-go"
)

// KafkaPublisher publishes events to Redpanda via kafka-go.
type KafkaPublisher struct {
	writer *kafka.Writer
	topic  string
}

// NewKafkaPublisher creates a publisher. If brokers is empty, returns a no-op publisher.
func NewKafkaPublisher(brokers, topic string) (*KafkaPublisher, error) {
	brokers = strings.TrimSpace(brokers)
	if brokers == "" {
		return &KafkaPublisher{topic: topic}, nil
	}
	writer := &kafka.Writer{
		Addr:         kafka.TCP(strings.Split(brokers, ",")...),
		Topic:        topic,
		Balancer:     &kafka.LeastBytes{},
		BatchSize:    10,
		BatchTimeout: 10 * time.Millisecond,
	}
	return &KafkaPublisher{writer: writer, topic: topic}, nil
}

// PublishUsage sends a usage event (non-blocking).
func (p *KafkaPublisher) PublishUsage(userID uuid.UUID, appName, category string, durationSeconds int64, recordedAt time.Time) {
	if p.writer == nil {
		return
	}
	ev := TelemetryEvent{
		EventID:         uuid.New().String(),
		EventType:       "usage",
		UserID:          userID.String(),
		RecordedAt:      recordedAt,
		AppName:         appName,
		Category:        category,
		DurationSeconds: durationSeconds,
	}
	p.publish(ev)
}

// PublishSession sends a session event (non-blocking).
func (p *KafkaPublisher) PublishSession(userID uuid.UUID, name, activityType string, startedAt time.Time, durationSeconds int64) {
	if p.writer == nil {
		return
	}
	ev := TelemetryEvent{
		EventID:         uuid.New().String(),
		EventType:       "session",
		UserID:          userID.String(),
		StartedAt:       startedAt,
		Name:            name,
		ActivityType:    activityType,
		DurationSeconds: durationSeconds,
	}
	p.publish(ev)
}

func (p *KafkaPublisher) publish(ev TelemetryEvent) {
	body, err := json.Marshal(ev)
	if err != nil {
		log.Printf("[telemetry] marshal event: %v", err)
		return
	}
	go func() {
		ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
		defer cancel()
		if err := p.writer.WriteMessages(ctx, kafka.Message{
			Key:   []byte(ev.EventID),
			Value: body,
		}); err != nil {
			log.Printf("[telemetry] write to kafka: %v", err)
		}
	}()
}

// Close flushes and closes the writer.
func (p *KafkaPublisher) Close() error {
	if p.writer == nil {
		return nil
	}
	return p.writer.Close()
}

// Ensure KafkaPublisher implements Publisher.
var _ Publisher = (*KafkaPublisher)(nil)
