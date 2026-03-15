// @title           Omni Gateway API
// @version         1.0
// @description     Unified API gateway: auth, tasks, usage, sessions.
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

	_ "omni-backend/cmd/gateway/docs"
	"omni-backend/internal/config"
	"omni-backend/internal/gateway"
	"omni-backend/internal/middleware"

	"github.com/gin-contrib/cors"
	"github.com/gin-gonic/gin"
	swaggerFiles "github.com/swaggo/files"
	ginSwagger "github.com/swaggo/gin-swagger"
)

func main() {
	cfg, err := config.LoadGateway()
	if err != nil {
		log.Fatalf("config: %v", err)
	}

	profileProxy, err := gateway.ReverseProxyTo(cfg.ProfileURL)
	if err != nil {
		log.Fatalf("profile proxy: %v", err)
	}
	taskProxy, err := gateway.ReverseProxyTo(cfg.TaskURL)
	if err != nil {
		log.Fatalf("task proxy: %v", err)
	}
	telemetryProxy, err := gateway.ReverseProxyTo(cfg.TelemetryURL)
	if err != nil {
		log.Fatalf("telemetry proxy: %v", err)
	}

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

	router.GET("/swagger/*any", ginSwagger.WrapHandler(swaggerFiles.Handler))
	api := router.Group("/api")
	auth := api.Group("/auth")
	{
		auth.POST("/register", gin.WrapH(profileProxy))
		auth.POST("/login", gin.WrapH(profileProxy))
		auth.GET("/me", middleware.AuthRequired(cfg.JWTSecret), gin.WrapH(profileProxy))
	}
	usage := api.Group("/usage").Use(middleware.AuthRequired(cfg.JWTSecret))
	usage.GET("", gin.WrapH(telemetryProxy))
	usage.POST("/sync", gin.WrapH(telemetryProxy))
	sessions := api.Group("/sessions").Use(middleware.AuthRequired(cfg.JWTSecret))
	sessions.GET("", gin.WrapH(telemetryProxy))
	sessions.POST("/sync", gin.WrapH(telemetryProxy))
	tasks := api.Group("/tasks").Use(middleware.AuthRequired(cfg.JWTSecret))
	tasks.GET("", gin.WrapH(taskProxy))
	tasks.POST("", gin.WrapH(taskProxy))
	tasks.Any("/*path", gin.WrapH(taskProxy))
	productivity := api.Group("/productivity").Use(middleware.AuthRequired(cfg.JWTSecret))
	productivity.GET("/notifications", gin.WrapH(telemetryProxy))
	productivity.PATCH("/notifications/:id/read", gin.WrapH(telemetryProxy))

	srv := &http.Server{Addr: ":" + cfg.Port, Handler: router}
	go func() {
		if err := srv.ListenAndServe(); err != nil && err != http.ErrServerClosed {
			log.Fatalf("server: %v", err)
		}
	}()
	waitForListen(":"+cfg.Port, 5*time.Second)
	log.Printf("gateway started successfully on port %s", cfg.Port)

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
