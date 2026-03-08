package middleware

import (
	"net/http"
	"strings"

	"omni-backend/internal/auth"

	"github.com/gin-gonic/gin"
)

const ContextKeyClaims = "claims"

func AuthRequired(jwtSecret string) gin.HandlerFunc {
	return func(c *gin.Context) {
		authHeader := c.GetHeader("Authorization")
		if authHeader == "" {
			c.AbortWithStatusJSON(http.StatusUnauthorized, gin.H{"error": "missing authorization header"})
			return
		}
		const prefix = "Bearer "
		if !strings.HasPrefix(authHeader, prefix) {
			c.AbortWithStatusJSON(http.StatusUnauthorized, gin.H{"error": "invalid authorization format"})
			return
		}
		tokenString := strings.TrimPrefix(authHeader, prefix)
		claims, err := auth.ValidateToken(tokenString, jwtSecret)
		if err != nil {
			c.AbortWithStatusJSON(http.StatusUnauthorized, gin.H{"error": "invalid or expired token"})
			return
		}
		c.Set(ContextKeyClaims, claims)
		c.Next()
	}
}

func GetClaims(c *gin.Context) *auth.Claims {
	v, _ := c.Get(ContextKeyClaims)
	claims, _ := v.(*auth.Claims)
	return claims
}
