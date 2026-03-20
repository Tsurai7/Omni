package config

import (
	"fmt"
	"os"
	"strconv"
	"strings"
	"time"
)

type Config struct {
	Port           string
	DatabaseURL    string
	JWTSecret      string
	JWTExpiryHours int
	// Kafka/Redpanda (optional; empty brokers = producer disabled)
	KafkaBrokers   string
	TelemetryTopic string
}

func Load() (*Config, error) {
	port := os.Getenv("PORT")
	if port == "" {
		port = "8080"
	}
	databaseURL := os.Getenv("DATABASE_URL")
	if databaseURL == "" {
		return nil, fmt.Errorf("DATABASE_URL is required")
	}
	jwtSecret := os.Getenv("JWT_SECRET")
	if jwtSecret == "" {
		return nil, fmt.Errorf("JWT_SECRET is required")
	}
	expiryHours := 24
	if s := os.Getenv("JWT_EXPIRY_HOURS"); s != "" {
		if h, err := strconv.Atoi(s); err == nil && h > 0 {
			expiryHours = h
		}
	}
	kafkaBrokers := os.Getenv("KAFKA_BROKERS")
	telemetryTopic := os.Getenv("TELEMETRY_TOPIC")
	if telemetryTopic == "" {
		telemetryTopic = "omni.telemetry.events"
	}
	return &Config{
		Port:           port,
		DatabaseURL:    databaseURL,
		JWTSecret:      jwtSecret,
		JWTExpiryHours: expiryHours,
		KafkaBrokers:   strings.TrimSpace(kafkaBrokers),
		TelemetryTopic: telemetryTopic,
	}, nil
}

func (c *Config) JWTExpiry() time.Duration {
	return time.Duration(c.JWTExpiryHours) * time.Hour
}

// GatewayConfig holds configuration for the API Gateway (no database).
type GatewayConfig struct {
	Port         string
	JWTSecret    string
	ProfileURL   string
	TaskURL      string
	TelemetryURL string
	AIURL        string // optional; empty = AI endpoints return 503
}

// LoadGateway loads config for the gateway from env. DATABASE_URL is not required.
func LoadGateway() (*GatewayConfig, error) {
	port := os.Getenv("PORT")
	if port == "" {
		port = "8080"
	}
	jwtSecret := os.Getenv("JWT_SECRET")
	if jwtSecret == "" {
		return nil, fmt.Errorf("JWT_SECRET is required")
	}
	profileURL := os.Getenv("PROFILE_URL")
	if profileURL == "" {
		return nil, fmt.Errorf("PROFILE_URL is required")
	}
	taskURL := os.Getenv("TASK_URL")
	if taskURL == "" {
		return nil, fmt.Errorf("TASK_URL is required")
	}
	telemetryURL := os.Getenv("TELEMETRY_URL")
	if telemetryURL == "" {
		return nil, fmt.Errorf("TELEMETRY_URL is required")
	}
	// AI service is optional — if not set, /api/ai/* routes return 503
	aiURL := strings.TrimSuffix(os.Getenv("AI_URL"), "/")
	return &GatewayConfig{
		Port:         port,
		JWTSecret:    jwtSecret,
		ProfileURL:   strings.TrimSuffix(profileURL, "/"),
		TaskURL:      strings.TrimSuffix(taskURL, "/"),
		TelemetryURL: strings.TrimSuffix(telemetryURL, "/"),
		AIURL:        aiURL,
	}, nil
}
