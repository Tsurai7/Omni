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
	"net"
	"net/http"
	"os"
	"os/signal"
	"syscall"
	"time"

	_ "omni-backend/cmd/gateway/docs"
	"omni-backend/internal/config"
	"omni-backend/internal/gateway"
	"omni-backend/internal/logger"
	"omni-backend/internal/middleware"

	"github.com/gin-contrib/cors"
	"github.com/gin-gonic/gin"
	swaggerFiles "github.com/swaggo/files"
	ginSwagger "github.com/swaggo/gin-swagger"
)

func main() {
	log := logger.New(os.Getenv("DEBUG") == "true")

	cfg, err := config.LoadGateway()
	if err != nil {
		log.Error("failed to load config", "error", err)
		os.Exit(1)
	}
	if cfg.AIURLHadTrailingAPISuffix {
		log.Info("AI_URL had trailing /api/ai; normalized to omni-ai service root (gateway forwards full /api/ai/... paths)", "ai_url", cfg.AIURL)
	}
	if err := gateway.CheckOmniAIAtStartup(cfg.AIURL, log); err != nil {
		log.Error("omni-ai startup check failed", "error", err)
		os.Exit(1)
	}

	profileProxy, err := gateway.ReverseProxyTo(cfg.ProfileURL, log)
	if err != nil {
		log.Error("failed to set up profile proxy", "error", err)
		os.Exit(1)
	}
	taskProxy, err := gateway.ReverseProxyTo(cfg.TaskURL, log)
	if err != nil {
		log.Error("failed to set up task proxy", "error", err)
		os.Exit(1)
	}
	telemetryProxy, err := gateway.ReverseProxyTo(cfg.TelemetryURL, log)
	if err != nil {
		log.Error("failed to set up telemetry proxy", "error", err)
		os.Exit(1)
	}
	var calendarProxy http.Handler
	if cfg.CalendarURL != "" {
		calendarProxy, err = gateway.ReverseProxyTo(cfg.CalendarURL, log)
		if err != nil {
			log.Error("failed to set up calendar proxy", "error", err)
			os.Exit(1)
		}
	} else {
		log.Info("Calendar service not configured, /api/calendar/* will return 503")
		calendarProxy = http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
			http.Error(w, `{"error":"Calendar service not configured"}`, http.StatusServiceUnavailable)
		})
	}

	var aiProxy http.Handler
	if cfg.AIURL != "" {
		aiProxy, err = gateway.ReverseProxyTo(cfg.AIURL, log)
		if err != nil {
			log.Error("failed to set up ai proxy", "error", err)
			os.Exit(1)
		}
	} else {
		log.Info("AI service not configured, /api/ai/* will return 503")
		aiProxy = http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
			http.Error(w, `{"error":"AI service not configured"}`, http.StatusServiceUnavailable)
		})
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
		auth.GET("/me", middleware.AuthRequired(cfg.JWTSecret, log), gin.WrapH(profileProxy))
	}
	usage := api.Group("/usage").Use(middleware.AuthRequired(cfg.JWTSecret, log))
	usage.GET("", gin.WrapH(telemetryProxy))
	usage.POST("/sync", gin.WrapH(telemetryProxy))
	sessions := api.Group("/sessions").Use(middleware.AuthRequired(cfg.JWTSecret, log))
	sessions.GET("", gin.WrapH(telemetryProxy))
	sessions.POST("/sync", gin.WrapH(telemetryProxy))
	tasks := api.Group("/tasks").Use(middleware.AuthRequired(cfg.JWTSecret, log))
	tasks.GET("", gin.WrapH(taskProxy))
	tasks.POST("", gin.WrapH(taskProxy))
	tasks.Any("/*path", gin.WrapH(taskProxy))
	productivity := api.Group("/productivity").Use(middleware.AuthRequired(cfg.JWTSecret, log))
	productivity.GET("/notifications", gin.WrapH(telemetryProxy))
	productivity.PATCH("/notifications/:id/read", gin.WrapH(telemetryProxy))
	// Public: Google OAuth callback (no auth — Google redirects here with code)
	api.GET("/calendar/auth/google/callback", gin.WrapH(calendarProxy))
	// Protected calendar routes (explicit — wildcard conflicts with the static callback above)
	calGroup := api.Group("/calendar").Use(middleware.AuthRequired(cfg.JWTSecret, log))
	calGroup.GET("/auth/google", gin.WrapH(calendarProxy))
	calGroup.POST("/auth/google/connect", gin.WrapH(calendarProxy))
	calGroup.DELETE("/auth/google", gin.WrapH(calendarProxy))
	calGroup.GET("/status", gin.WrapH(calendarProxy))
	calGroup.GET("/events", gin.WrapH(calendarProxy))
	calGroup.POST("/sync", gin.WrapH(calendarProxy))
	ai := api.Group("/ai").Use(middleware.AuthRequired(cfg.JWTSecret, log))
	ai.Any("/*path", gin.WrapH(aiProxy))

	srv := &http.Server{Addr: ":" + cfg.Port, Handler: router}
	go func() {
		if err := srv.ListenAndServe(); err != nil && err != http.ErrServerClosed {
			log.Error("server listen failed", "error", err)
			os.Exit(1)
		}
	}()
	waitForListen(":"+cfg.Port, 5*time.Second)
	log.Info("gateway started", "port", cfg.Port)
	if cfg.AIURL != "" {
		gateway.LogAIReachability(cfg.AIURL, log)
	}

	quit := make(chan os.Signal, 1)
	signal.Notify(quit, syscall.SIGINT, syscall.SIGTERM)
	<-quit
	log.Info("shutting down gateway")
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
