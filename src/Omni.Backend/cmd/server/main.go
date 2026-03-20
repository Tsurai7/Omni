package main

import (
	"context"
	"net/http"
	"os"
	"os/signal"
	"syscall"

	"omni-backend/internal/config"
	"omni-backend/internal/db"
	"omni-backend/internal/handlers"
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
	router := gin.Default()
	router.Use(cors.New(cors.Config{
		AllowOrigins:     []string{"*"},
		AllowMethods:     []string{"GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS"},
		AllowHeaders:     []string{"Origin", "Content-Type", "Authorization"},
		ExposeHeaders:    []string{"Content-Length"},
		AllowCredentials: true,
	}))

	authHandler := handlers.NewAuthHandler(pool, cfg.JWTSecret, cfg.JWTExpiry(), log)
	usageHandler := handlers.NewUsageHandler(pool, log)
	sessionsHandler := handlers.NewSessionsHandler(pool, log)
	api := router.Group("/api")
	auth := api.Group("/auth")
	{
		auth.POST("/register", authHandler.Register)
		auth.POST("/login", authHandler.Login)
		auth.GET("/me", middleware.AuthRequired(cfg.JWTSecret, log), authHandler.Me)
	}
	api.Group("/usage").Use(middleware.AuthRequired(cfg.JWTSecret, log)).
		POST("/sync", usageHandler.Sync).
		GET("", usageHandler.List)
	api.Group("/sessions").Use(middleware.AuthRequired(cfg.JWTSecret, log)).
		POST("/sync", sessionsHandler.Sync).
		GET("", sessionsHandler.List)

	srv := &http.Server{Addr: ":" + cfg.Port, Handler: router}
	go func() {
		if err := srv.ListenAndServe(); err != nil && err != http.ErrServerClosed {
			log.Error("server listen failed", "error", err)
			os.Exit(1)
		}
	}()
	log.Info("server started", "port", cfg.Port)

	quit := make(chan os.Signal, 1)
	signal.Notify(quit, syscall.SIGINT, syscall.SIGTERM)
	<-quit
	log.Info("shutting down server")
	if err := srv.Shutdown(context.Background()); err != nil {
		log.Error("shutdown error", "error", err)
	}
}
