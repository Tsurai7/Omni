package profile

import (
	"errors"
	"log/slog"
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

type Handler struct {
	Pool      *pgxpool.Pool
	JWTSecret string
	JWTExpiry time.Duration
	logger    *slog.Logger
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

func NewHandler(pool *pgxpool.Pool, jwtSecret string, jwtExpiry time.Duration, logger *slog.Logger) *Handler {
	return &Handler{Pool: pool, JWTSecret: jwtSecret, JWTExpiry: jwtExpiry, logger: logger}
}

// Register godoc
// @Summary      Register a new user
// @Tags         auth
// @Accept       json
// @Produce      json
// @Param        body  body  registerRequest  true  "Email and password"
// @Success      201   {object}  registerResponse
// @Failure      400   {object}  map[string]string
// @Failure      409   {object}  map[string]string
// @Router       /auth/register [post]
func (h *Handler) Register(c *gin.Context) {
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
			h.logger.Warn("registration attempted with existing email", "email", req.Email)
			c.JSON(http.StatusConflict, gin.H{"error": "email already registered"})
			return
		}
		h.logger.Error("failed to create user account", "error", err)
		c.JSON(http.StatusInternalServerError, gin.H{"error": "failed to create account"})
		return
	}
	token, expiresAt, err := auth.GenerateToken(userID, req.Email, h.JWTSecret, h.JWTExpiry)
	if err != nil {
		h.logger.Error("failed to generate token after registration", "user_id", userID, "error", err)
		c.JSON(http.StatusInternalServerError, gin.H{"error": "failed to issue token"})
		return
	}
	h.logger.Info("user registered", "user_id", userID, "email", req.Email)
	c.JSON(http.StatusCreated, registerResponse{
		ID:        userID.String(),
		Email:     req.Email,
		Token:     token,
		ExpiresAt: expiresAt.Format("2006-01-02T15:04:05Z07:00"),
	})
}

// Login godoc
// @Summary      Log in
// @Tags         auth
// @Accept       json
// @Produce      json
// @Param        body  body  loginRequest  true  "Email and password"
// @Success      200   {object}  tokenResponse
// @Failure      401   {object}  map[string]string
// @Router       /auth/login [post]
func (h *Handler) Login(c *gin.Context) {
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
			h.logger.Warn("login failed: user not found", "email", req.Email)
			c.JSON(http.StatusUnauthorized, gin.H{"error": "invalid email or password"})
			return
		}
		h.logger.Error("login db query failed", "error", err)
		c.JSON(http.StatusInternalServerError, gin.H{"error": "login failed"})
		return
	}
	if err := auth.ComparePassword(u.PasswordHash, req.Password); err != nil {
		h.logger.Warn("login failed: wrong password", "email", req.Email)
		c.JSON(http.StatusUnauthorized, gin.H{"error": "invalid email or password"})
		return
	}
	token, expiresAt, err := auth.GenerateToken(u.ID, u.Email, h.JWTSecret, h.JWTExpiry)
	if err != nil {
		h.logger.Error("failed to generate token after login", "user_id", u.ID, "error", err)
		c.JSON(http.StatusInternalServerError, gin.H{"error": "failed to issue token"})
		return
	}
	h.logger.Info("user logged in", "user_id", u.ID, "email", req.Email)
	c.JSON(http.StatusOK, tokenResponse{
		Token:     token,
		ExpiresAt: expiresAt.Format("2006-01-02T15:04:05Z07:00"),
	})
}

// Me godoc
// @Summary      Get current user
// @Tags         auth
// @Security     BearerAuth
// @Produce      json
// @Success      200  {object}  userResponse
// @Failure      401  {object}  map[string]string
// @Router       /auth/me [get]
func (h *Handler) Me(c *gin.Context) {
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
