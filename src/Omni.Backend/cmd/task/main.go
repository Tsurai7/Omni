// @title           Omni Task API
// @version         1.0
// @description     Task list, create, update status, delete.
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

	_ "omni-backend/cmd/task/docs"
	"omni-backend/internal/config"
	"omni-backend/internal/db"
	"omni-backend/internal/logger"
	"omni-backend/internal/middleware"
	"omni-backend/internal/task"

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

	h := task.NewHandler(pool, log)
	router.GET("/swagger/*any", ginSwagger.WrapHandler(swaggerFiles.Handler))
	api := router.Group("/api")
	tasks := api.Group("/tasks").Use(middleware.AuthRequired(cfg.JWTSecret, log))
	{
		tasks.GET("", h.List)
		tasks.POST("", h.Create)
		tasks.PUT("/:id", h.Update)
		tasks.PATCH("/:id/status", h.UpdateStatus)
		tasks.DELETE("/:id", h.Delete)
	}

	srv := &http.Server{Addr: ":" + cfg.Port, Handler: router}
	go func() {
		if err := srv.ListenAndServe(); err != nil && err != http.ErrServerClosed {
			log.Error("server listen failed", "error", err)
			os.Exit(1)
		}
	}()
	waitForListen(":"+cfg.Port, 5*time.Second)
	log.Info("task service started", "port", cfg.Port)

	quit := make(chan os.Signal, 1)
	signal.Notify(quit, syscall.SIGINT, syscall.SIGTERM)
	<-quit
	log.Info("shutting down task service")
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
