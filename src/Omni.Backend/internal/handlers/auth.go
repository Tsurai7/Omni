package handlers

import (
	"errors"
	"net/http"
	"regexp"
	"strings"
	"time"

	"omni-backend/internal/auth"
	"omni-backend/internal/middleware"
	"omni-backend/internal/models"

	"github.com/gin-gonic/gin"
	"github.com/google/uuid"
	"github.com/jackc/pgx/v5"
	"github.com/jackc/pgx/v5/pgconn"
	"github.com/jackc/pgx/v5/pgxpool"
)

var emailRegex = regexp.MustCompile(`^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$`)

const minPasswordLen = 8

type AuthHandler struct {
	Pool      *pgxpool.Pool
	JWTSecret string
	JWTExpiry time.Duration
}

type registerRequest struct {
	Email    string `json:"email"`
	Password string `json:"password"`
}

type loginRequest struct {
	Email    string `json:"email"`
	Password string `json:"password"`
}

type tokenResponse struct {
	Token     string `json:"token"`
	ExpiresAt string `json:"expires_at"`
}

type userResponse struct {
	ID    string `json:"id"`
	Email string `json:"email"`
}

type registerResponse struct {
	ID        string `json:"id"`
	Email     string `json:"email"`
	Token     string `json:"token"`
	ExpiresAt string `json:"expires_at"`
}

func NewAuthHandler(pool *pgxpool.Pool, jwtSecret string, jwtExpiry time.Duration) *AuthHandler {
	return &AuthHandler{Pool: pool, JWTSecret: jwtSecret, JWTExpiry: jwtExpiry}
}

func (h *AuthHandler) Register(c *gin.Context) {
	var req registerRequest
	if err := c.ShouldBindJSON(&req); err != nil {
		c.JSON(http.StatusBadRequest, gin.H{"error": "invalid request body"})
		return
	}
	req.Email = strings.TrimSpace(strings.ToLower(req.Email))
	if !emailRegex.MatchString(req.Email) {
		c.JSON(http.StatusBadRequest, gin.H{"error": "invalid email format"})
		return
	}
	if len(req.Password) < minPasswordLen {
		c.JSON(http.StatusBadRequest, gin.H{"error": "password must be at least 8 characters"})
		return
	}
	hash, err := auth.HashPassword(req.Password)
	if err != nil {
		c.JSON(http.StatusInternalServerError, gin.H{"error": "failed to process password"})
		return
	}
	userID := uuid.New()
	ctx := c.Request.Context()
	_, err = h.Pool.Exec(ctx, `INSERT INTO users (id, email, password_hash) VALUES ($1, $2, $3)`,
		userID, req.Email, hash)
	if err != nil {
		if isUniqueViolation(err) {
			c.JSON(http.StatusConflict, gin.H{"error": "email already registered"})
			return
		}
		c.JSON(http.StatusInternalServerError, gin.H{"error": "failed to create account"})
		return
	}
	token, expiresAt, err := auth.GenerateToken(userID, req.Email, h.JWTSecret, h.JWTExpiry)
	if err != nil {
		c.JSON(http.StatusInternalServerError, gin.H{"error": "failed to issue token"})
		return
	}
	c.JSON(http.StatusCreated, registerResponse{
		ID:        userID.String(),
		Email:     req.Email,
		Token:     token,
		ExpiresAt: expiresAt.Format("2006-01-02T15:04:05Z07:00"),
	})
}

func (h *AuthHandler) Login(c *gin.Context) {
	var req loginRequest
	if err := c.ShouldBindJSON(&req); err != nil {
		c.JSON(http.StatusBadRequest, gin.H{"error": "invalid request body"})
		return
	}
	req.Email = strings.TrimSpace(strings.ToLower(req.Email))
	var u models.User
	ctx := c.Request.Context()
	err := h.Pool.QueryRow(ctx, `SELECT id, email, password_hash, created_at FROM users WHERE email = $1`, req.Email).
		Scan(&u.ID, &u.Email, &u.PasswordHash, &u.CreatedAt)
	if err != nil {
		if err == pgx.ErrNoRows {
			c.JSON(http.StatusUnauthorized, gin.H{"error": "invalid email or password"})
			return
		}
		c.JSON(http.StatusInternalServerError, gin.H{"error": "login failed"})
		return
	}
	if err := auth.ComparePassword(u.PasswordHash, req.Password); err != nil {
		c.JSON(http.StatusUnauthorized, gin.H{"error": "invalid email or password"})
		return
	}
	token, expiresAt, err := auth.GenerateToken(u.ID, u.Email, h.JWTSecret, h.JWTExpiry)
	if err != nil {
		c.JSON(http.StatusInternalServerError, gin.H{"error": "failed to issue token"})
		return
	}
	c.JSON(http.StatusOK, tokenResponse{
		Token:     token,
		ExpiresAt: expiresAt.Format("2006-01-02T15:04:05Z07:00"),
	})
}

func (h *AuthHandler) Me(c *gin.Context) {
	claims := middleware.GetClaims(c)
	if claims == nil {
		c.JSON(http.StatusUnauthorized, gin.H{"error": "unauthorized"})
		return
	}
	c.JSON(http.StatusOK, userResponse{
		ID:    claims.UserID,
		Email: claims.Email,
	})
}

func isUniqueViolation(err error) bool {
	var pgErr *pgconn.PgError
	return errors.As(err, &pgErr) && pgErr.Code == "23505"
}
