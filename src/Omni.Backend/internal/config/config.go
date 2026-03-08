package config

import (
	"fmt"
	"os"
	"strconv"
	"time"
)

type Config struct {
	Port           string
	DatabaseURL    string
	JWTSecret      string
	JWTExpiryHours int
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
	return &Config{
		Port:           port,
		DatabaseURL:    databaseURL,
		JWTSecret:      jwtSecret,
		JWTExpiryHours: expiryHours,
	}, nil
}

func (c *Config) JWTExpiry() time.Duration {
	return time.Duration(c.JWTExpiryHours) * time.Hour
}
