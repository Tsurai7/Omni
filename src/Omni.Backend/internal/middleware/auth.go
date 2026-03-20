package middleware

import (
	"log/slog"
	"net/http"
	"strings"

	"omni-backend/internal/auth"

	"github.com/gin-gonic/gin"
)

const ContextKeyClaims = "claims"

func AuthRequired(jwtSecret string, logger *slog.Logger) gin.HandlerFunc {
	return func(c *gin.Context) {
		authHeader := c.GetHeader("Authorization")
		if authHeader == "" {
			logger.Warn("missing authorization header", "path", c.Request.URL.Path)
			c.AbortWithStatusJSON(http.StatusUnauthorized, gin.H{"error": "missing authorization header"})
			return
		}
		const prefix = "Bearer "
		if !strings.HasPrefix(authHeader, prefix) {
			logger.Warn("invalid authorization format", "path", c.Request.URL.Path)
			c.AbortWithStatusJSON(http.StatusUnauthorized, gin.H{"error": "invalid authorization format"})
			return
		}
		tokenString := strings.TrimPrefix(authHeader, prefix)
		claims, err := auth.ValidateToken(tokenString, jwtSecret)
		if err != nil {
			logger.Warn("token validation failed", "path", c.Request.URL.Path, "error", err)
			c.AbortWithStatusJSON(http.StatusUnauthorized, gin.H{"error": "invalid or expired token"})
			return
		}
		logger.Debug("token validated", "user_id", claims.UserID, "path", c.Request.URL.Path)
		c.Set(ContextKeyClaims, claims)
		c.Next()
	}
}

func GetClaims(c *gin.Context) *auth.Claims {
	v, _ := c.Get(ContextKeyClaims)
	claims, _ := v.(*auth.Claims)
	return claims
}
