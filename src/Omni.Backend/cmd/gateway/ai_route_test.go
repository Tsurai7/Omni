package main

import (
	"context"
	"net/http"
	"net/http/httptest"
	"testing"

	"github.com/gin-gonic/gin"
)

func TestAICatchAllRouteMatchesNestedPath(t *testing.T) {
	gin.SetMode(gin.TestMode)
	r := gin.New()
	api := r.Group("/api")
	ai := api.Group("/ai")
	ai.Any("/*path", func(c *gin.Context) {
		c.String(http.StatusOK, c.Param("path"))
	})

	req := httptest.NewRequestWithContext(context.Background(), http.MethodGet, "/api/ai/chat/c6c32e87-05d5-447c-870d-e14a2b302f0b/starters", nil)
	w := httptest.NewRecorder()
	r.ServeHTTP(w, req)
	if w.Code != http.StatusOK {
		t.Fatalf("expected 200, got %d body=%q", w.Code, w.Body.String())
	}
}
