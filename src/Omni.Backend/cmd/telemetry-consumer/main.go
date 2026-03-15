package main

import (
	"context"
	"encoding/json"
	"log"
	"net"
	"os"
	"os/signal"
	"strings"
	"syscall"
	"time"

	"github.com/ClickHouse/clickhouse-go/v2"
	"github.com/ClickHouse/clickhouse-go/v2/lib/driver"
	"github.com/google/uuid"
	"github.com/segmentio/kafka-go"
	"omni-backend/internal/telemetry"
)

func waitForKafka(brokers string, maxWait time.Duration) {
	brokerList := strings.Split(strings.TrimSpace(brokers), ",")
	if len(brokerList) == 0 {
		return
	}
	first := strings.TrimSpace(brokerList[0])
	if first == "" {
		return
	}
	deadline := time.Now().Add(maxWait)
	for time.Now().Before(deadline) {
		c, err := net.DialTimeout("tcp", first, 2*time.Second)
		if err == nil {
			c.Close()
			log.Printf("Kafka reachable at %s", first)
			return
		}
		log.Printf("Waiting for Kafka at %s (%v), retrying in 5s...", first, err)
		time.Sleep(5 * time.Second)
	}
	log.Fatalf("Kafka at %s not reachable after %v", first, maxWait)
}

func main() {
	kafkaBrokers := strings.TrimSpace(os.Getenv("KAFKA_BROKERS"))
	if kafkaBrokers == "" {
		log.Fatal("KAFKA_BROKERS is required")
	}
	topic := os.Getenv("TELEMETRY_TOPIC")
	if topic == "" {
		topic = "omni.telemetry.events"
	}
	chHost := os.Getenv("CLICKHOUSE_HOST")
	if chHost == "" {
		chHost = "localhost"
	}
	chPort := os.Getenv("CLICKHOUSE_PORT")
	if chPort == "" {
		chPort = "9000"
	}
	chUser := os.Getenv("CLICKHOUSE_USER")
	if chUser == "" {
		chUser = "default"
	}
	chPassword := os.Getenv("CLICKHOUSE_PASSWORD")
	chDB := os.Getenv("CLICKHOUSE_DB")
	if chDB == "" {
		chDB = "omni_analytics"
	}

	waitForKafka(kafkaBrokers, 2*time.Minute)

	ctx := context.Background()
	conn, err := clickhouse.Open(&clickhouse.Options{
		Addr: []string{chHost + ":" + chPort},
		Auth: clickhouse.Auth{
			Database: chDB,
			Username: chUser,
			Password: chPassword,
		},
		DialTimeout: 10 * time.Second,
	})
	if err != nil {
		log.Fatalf("clickhouse open: %v", err)
	}
	defer conn.Close()
	if err := conn.Ping(ctx); err != nil {
		log.Fatalf("clickhouse ping: %v", err)
	}

	if err := ensureSchema(ctx, conn, chDB); err != nil {
		log.Fatalf("clickhouse schema: %v", err)
	}

	reader := kafka.NewReader(kafka.ReaderConfig{
		Brokers:        strings.Split(kafkaBrokers, ","),
		Topic:          topic,
		GroupID:        "omni-telemetry-consumer",
		MinBytes:       1,
		MaxBytes:       10 << 20,
		MaxWait:        time.Second,
		CommitInterval: time.Second,
	})
	defer reader.Close()

	log.Printf("telemetry-consumer started successfully (topic=%s, clickhouse=%s:%s)", topic, chHost, chPort)

	ctx, cancel := context.WithCancel(ctx)
	defer cancel()
	go func() {
		sig := make(chan os.Signal, 1)
		signal.Notify(sig, syscall.SIGINT, syscall.SIGTERM)
		<-sig
		cancel()
	}()

	type batchItem struct {
		ev  telemetry.TelemetryEvent
		msg kafka.Message
	}
	var batch []batchItem
	const batchSize = 100
	ticker := time.NewTicker(time.Second)
	defer ticker.Stop()

	flush := func() {
		if len(batch) == 0 {
			return
		}
		events := make([]telemetry.TelemetryEvent, len(batch))
		for i := range batch {
			events[i] = batch[i].ev
		}
		if err := insertBatch(ctx, conn, chDB, events); err != nil {
			log.Printf("insert batch: %v", err)
			return
		}
		msgs := make([]kafka.Message, len(batch))
		for i := range batch {
			msgs[i] = batch[i].msg
		}
		if err := reader.CommitMessages(ctx, msgs...); err != nil {
			log.Printf("commit offsets: %v", err)
		}
		log.Printf("inserted %d events", len(batch))
		batch = batch[:0]
	}

	for {
		m, err := reader.ReadMessage(ctx)
		if err != nil {
			if ctx.Err() != nil {
				flush()
				return
			}
			log.Printf("read message: %v", err)
			continue
		}
		var ev telemetry.TelemetryEvent
		if err := json.Unmarshal(m.Value, &ev); err != nil {
			log.Printf("unmarshal: %v", err)
			continue
		}
		batch = append(batch, batchItem{ev: ev, msg: m})
		if len(batch) >= batchSize {
			flush()
		}
		select {
		case <-ticker.C:
			flush()
		default:
		}
	}
}

func ensureSchema(ctx context.Context, conn driver.Conn, db string) error {
	if err := conn.Exec(ctx, "CREATE DATABASE IF NOT EXISTS "+db); err != nil {
		return err
	}
	ddl := `CREATE TABLE IF NOT EXISTS ` + db + `.telemetry_events (
		event_id UUID,
		event_type String,
		user_id UUID,
		at DateTime64(3),
		recorded_at Nullable(DateTime64(3)),
		started_at Nullable(DateTime64(3)),
		app_name Nullable(String),
		category Nullable(String),
		name Nullable(String),
		activity_type Nullable(String),
		duration_seconds Int64
	) ENGINE = MergeTree()
	ORDER BY (user_id, at, event_id)`
	return conn.Exec(ctx, ddl)
}

func insertBatch(ctx context.Context, conn driver.Conn, db string, events []telemetry.TelemetryEvent) error {
	batch, err := conn.PrepareBatch(ctx, "INSERT INTO "+db+".telemetry_events (event_id, event_type, user_id, at, recorded_at, started_at, app_name, category, name, activity_type, duration_seconds)")
	if err != nil {
		return err
	}
	defer batch.Abort()

	for _, ev := range events {
		eventID, _ := uuid.Parse(ev.EventID)
		userID, _ := uuid.Parse(ev.UserID)
		var at time.Time
		var recordedAt, startedAt *time.Time
		if ev.EventType == "usage" && !ev.RecordedAt.IsZero() {
			at = ev.RecordedAt
			t := ev.RecordedAt
			recordedAt = &t
		} else if !ev.StartedAt.IsZero() {
			at = ev.StartedAt
			t := ev.StartedAt
			startedAt = &t
		} else {
			at = time.Now().UTC()
		}
		var appName, category, name, activityType *string
		if ev.AppName != "" {
			appName = &ev.AppName
		}
		if ev.Category != "" {
			category = &ev.Category
		}
		if ev.Name != "" {
			name = &ev.Name
		}
		if ev.ActivityType != "" {
			activityType = &ev.ActivityType
		}
		if err := batch.Append(eventID, ev.EventType, userID, at, recordedAt, startedAt, appName, category, name, activityType, ev.DurationSeconds); err != nil {
			return err
		}
	}
	return batch.Send()
}
