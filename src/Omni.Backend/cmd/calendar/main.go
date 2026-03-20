package main

import (
	"context"
	"net"
	"net/http"
	"os"
	"os/signal"
	"syscall"
	"time"

	"omni-backend/internal/calendar"
	"omni-backend/internal/config"
	"omni-backend/internal/db"
	"omni-backend/internal/logger"
	"omni-backend/internal/middleware"

	"github.com/gin-contrib/cors"
	"github.com/gin-gonic/gin"
)

func main() {
	log := logger.New(os.Getenv("DEBUG") == "true")

	cfg, err := config.Load()
	if err != nil {
		log.Error("failed to load config", "error", err)
		os.Exit(1)
	}

	// Google OAuth config
	googleClientID := os.Getenv("GOOGLE_OAUTH_CLIENT_ID")
	googleClientSecret := os.Getenv("GOOGLE_OAUTH_CLIENT_SECRET")
	googleRedirectURI := os.Getenv("GOOGLE_OAUTH_REDIRECT_URI")
	if googleRedirectURI == "" {
		googleRedirectURI = "omni://calendar/connected"
	}

	ctx := context.Background()
	pool, err := db.NewPool(ctx, cfg.DatabaseURL)
	if err != nil {
		log.Error("failed to connect to database", "error", err)
		os.Exit(1)
	}
	defer pool.Close()
	log.Info("database connected")

	if err := db.Migrate(ctx, pool); err != nil {
		log.Error("failed to run migrations", "error", err)
		os.Exit(1)
	}
	log.Info("migrations complete")

	if os.Getenv("GIN_MODE") != "debug" {
		gin.SetMode(gin.ReleaseMode)
	}

	googleClient := calendar.NewGoogleClient(googleClientID, googleClientSecret, googleRedirectURI)
	h := calendar.NewHandler(pool, googleClient, log)

	router := gin.Default()
	router.Use(cors.New(cors.Config{
		AllowOrigins:     []string{"*"},
		AllowMethods:     []string{"GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS"},
		AllowHeaders:     []string{"Origin", "Content-Type", "Authorization"},
		ExposeHeaders:    []string{"Content-Length"},
		AllowCredentials: true,
	}))

	api := router.Group("/api")
	calGroup := api.Group("/calendar").Use(middleware.AuthRequired(cfg.JWTSecret, log))
	{
		calGroup.GET("/auth/google", h.GetAuthURL)
		calGroup.POST("/auth/google/connect", h.Connect)
		calGroup.DELETE("/auth/google", h.Disconnect)
		calGroup.GET("/status", h.Status)
		calGroup.GET("/events", h.ListEvents)
		calGroup.POST("/sync", h.Sync)
	}

	srv := &http.Server{Addr: ":" + cfg.Port, Handler: router}
	go func() {
		if err := srv.ListenAndServe(); err != nil && err != http.ErrServerClosed {
			log.Error("server listen failed", "error", err)
			os.Exit(1)
		}
	}()
	waitForListen(":"+cfg.Port, 5*time.Second)
	log.Info("calendar service started", "port", cfg.Port)

	quit := make(chan os.Signal, 1)
	signal.Notify(quit, syscall.SIGINT, syscall.SIGTERM)
	<-quit
	log.Info("shutting down calendar service")
	if err := srv.Shutdown(context.Background()); err != nil {
		log.Error("shutdown error", "error", err)
	}
}

func waitForListen(addr string, timeout time.Duration) {
	deadline := time.Now().Add(timeout)
	for time.Now().Before(deadline) {
		c, err := net.DialTimeout("tcp", addr, time.Second)
		if err == nil {
			c.Close()
			return
		}
		time.Sleep(100 * time.Millisecond)
	}
}
