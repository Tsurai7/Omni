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
	"log"
	"net"
	"net/http"
	"os"
	"os/signal"
	"syscall"
	"time"

	_ "omni-backend/cmd/telemetry/docs"
	"omni-backend/internal/config"
	"omni-backend/internal/db"
	"omni-backend/internal/middleware"
	"omni-backend/internal/telemetry"

	"github.com/gin-contrib/cors"
	"github.com/gin-gonic/gin"
	swaggerFiles "github.com/swaggo/files"
	ginSwagger "github.com/swaggo/gin-swagger"
)

func main() {
	cfg, err := config.Load()
	if err != nil {
		log.Fatalf("config: %v", err)
	}

	ctx := context.Background()
	pool, err := db.NewPool(ctx, cfg.DatabaseURL)
	if err != nil {
		log.Fatalf("database: %v", err)
	}
	defer pool.Close()

	if err := db.Migrate(ctx, pool); err != nil {
		log.Fatalf("migrate: %v", err)
	}

	publisher, err := telemetry.NewKafkaPublisher(cfg.KafkaBrokers, cfg.TelemetryTopic)
	if err != nil {
		log.Fatalf("kafka publisher: %v", err)
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

	usageHandler := telemetry.NewUsageHandler(pool, publisher)
	sessionsHandler := telemetry.NewSessionsHandler(pool, publisher)
	router.GET("/swagger/*any", ginSwagger.WrapHandler(swaggerFiles.Handler))
	api := router.Group("/api")
	api.Group("/usage").Use(middleware.AuthRequired(cfg.JWTSecret)).
		POST("/sync", usageHandler.Sync).
		GET("", usageHandler.List)
	api.Group("/sessions").Use(middleware.AuthRequired(cfg.JWTSecret)).
		POST("/sync", sessionsHandler.Sync).
		GET("", sessionsHandler.List)

	srv := &http.Server{Addr: ":" + cfg.Port, Handler: router}
	go func() {
		if err := srv.ListenAndServe(); err != nil && err != http.ErrServerClosed {
			log.Fatalf("server: %v", err)
		}
	}()
	waitForListen(":"+cfg.Port, 5*time.Second)
	if cfg.KafkaBrokers != "" {
		log.Printf("telemetry service started successfully on port %s (Kafka: %s)", cfg.Port, cfg.KafkaBrokers)
	} else {
		log.Printf("telemetry service started successfully on port %s (Kafka disabled)", cfg.Port)
	}

	quit := make(chan os.Signal, 1)
	signal.Notify(quit, syscall.SIGINT, syscall.SIGTERM)
	<-quit
	log.Println("shutting down...")
	if err := srv.Shutdown(context.Background()); err != nil {
		log.Printf("shutdown: %v", err)
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
