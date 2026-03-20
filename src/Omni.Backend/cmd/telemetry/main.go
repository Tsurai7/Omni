// @title           Omni Telemetry API
// @version         1.0
// @description     Usage and sessions sync and list.
// @BasePath        /api
// @securityDefinitions.apikey  BearerAuth
// @in                          header
// @name                        Authorization

package main

import (
	"context"
	"net"
	"net/http"
	"os"
	"os/signal"
	"syscall"
	"time"

	_ "omni-backend/cmd/telemetry/docs"
	"omni-backend/internal/config"
	"omni-backend/internal/db"
	"omni-backend/internal/logger"
	"omni-backend/internal/middleware"
	"omni-backend/internal/telemetry"

	"github.com/gin-contrib/cors"
	"github.com/gin-gonic/gin"
	swaggerFiles "github.com/swaggo/files"
	ginSwagger "github.com/swaggo/gin-swagger"
)

func main() {
	log := logger.New(os.Getenv("DEBUG") == "true")

	cfg, err := config.Load()
	if err != nil {
		log.Error("failed to load config", "error", err)
		os.Exit(1)
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

	publisher, err := telemetry.NewKafkaPublisher(cfg.KafkaBrokers, cfg.TelemetryTopic, log)
	if err != nil {
		log.Error("failed to create kafka publisher", "error", err)
		os.Exit(1)
	}
	defer publisher.Close()

	if os.Getenv("GIN_MODE") != "debug" {
		gin.SetMode(gin.ReleaseMode)
	}
	router := gin.Default()
	router.Use(cors.New(cors.Config{
		AllowOrigins:     []string{"*"},
		AllowMethods:     []string{"GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS"},
		AllowHeaders:     []string{"Origin", "Content-Type", "Authorization"},
		ExposeHeaders:    []string{"Content-Length"},
		AllowCredentials: true,
	}))

	usageHandler := telemetry.NewUsageHandler(pool, publisher, log)
	sessionsHandler := telemetry.NewSessionsHandler(pool, publisher, log)
	notificationsHandler := telemetry.NewNotificationsHandler(pool, log)
	router.GET("/swagger/*any", ginSwagger.WrapHandler(swaggerFiles.Handler))
	api := router.Group("/api")
	api.Group("/usage").Use(middleware.AuthRequired(cfg.JWTSecret, log)).
		POST("/sync", usageHandler.Sync).
		GET("", usageHandler.List)
	api.Group("/sessions").Use(middleware.AuthRequired(cfg.JWTSecret, log)).
		POST("/sync", sessionsHandler.Sync).
		GET("", sessionsHandler.List)
	api.Group("/productivity").Use(middleware.AuthRequired(cfg.JWTSecret, log)).
		GET("/notifications", notificationsHandler.List).
		PATCH("/notifications/:id/read", notificationsHandler.MarkRead)

	srv := &http.Server{Addr: ":" + cfg.Port, Handler: router}
	go func() {
		if err := srv.ListenAndServe(); err != nil && err != http.ErrServerClosed {
			log.Error("server listen failed", "error", err)
			os.Exit(1)
		}
	}()
	waitForListen(":"+cfg.Port, 5*time.Second)
	log.Info("telemetry service started", "port", cfg.Port, "kafka_enabled", cfg.KafkaBrokers != "")

	quit := make(chan os.Signal, 1)
	signal.Notify(quit, syscall.SIGINT, syscall.SIGTERM)
	<-quit
	log.Info("shutting down telemetry service")
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
