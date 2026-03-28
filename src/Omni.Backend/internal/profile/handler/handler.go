// Package handler wires the profile HTTP routes to the AuthService.
package handler

import (
	"errors"
	"log/slog"
	"net/http"

	"omni-backend/internal/middleware"
	"omni-backend/internal/profile/domain"
	"omni-backend/internal/profile/service"

	"github.com/gin-gonic/gin"
	"github.com/google/uuid"
)

// Handler is the HTTP adapter for the auth/profile service.
type Handler struct {
	svc    service.AuthService
	logger *slog.Logger
}

// New returns a Handler wired to the given AuthService.
func New(svc service.AuthService, logger *slog.Logger) *Handler {
	return &Handler{svc: svc, logger: logger}
}

// RegisterRoutes attaches auth routes to the given groups.
// publicGroup should have no auth middleware.
// protectedGroup should have JWT middleware applied.
func (h *Handler) RegisterRoutes(publicGroup, protectedGroup *gin.RouterGroup) {
	publicGroup.POST("/register", h.Register)
	publicGroup.POST("/login", h.Login)
	protectedGroup.GET("/me", h.Me)
}

// ---- request / response types ----

type registerRequest struct {
	Email    string `json:"email"    binding:"required"`
	Password string `json:"password" binding:"required"`
}

type loginRequest struct {
	Email    string `json:"email"    binding:"required"`
	Password string `json:"password" binding:"required"`
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

type errBody struct {
	Error string `json:"error"`
}

func errResp(msg string) errBody { return errBody{Error: msg} }

// ---- handlers ----

// Register godoc
// @Summary      Register a new user
// @Tags         auth
// @Accept       json
// @Produce      json
// @Param        body  body  registerRequest  true  "Email and password"
// @Success      201   {object}  registerResponse
// @Failure      400   {object}  errBody
// @Failure      409   {object}  errBody
// @Router       /auth/register [post]
func (h *Handler) Register(c *gin.Context) {
	var req registerRequest
	if err := c.ShouldBindJSON(&req); err != nil {
		c.JSON(http.StatusBadRequest, errResp("invalid request body"))
		return
	}

	res, err := h.svc.Register(c.Request.Context(), service.RegisterCmd{
		Email:    req.Email,
		Password: req.Password,
	})
	if err != nil {
		h.handleErr(c, err)
		return
	}

	c.JSON(http.StatusCreated, registerResponse{
		ID:        res.User.ID.String(),
		Email:     res.User.Email,
		Token:     res.Token,
		ExpiresAt: res.ExpiresAt.Format("2006-01-02T15:04:05Z07:00"),
	})
}

// Login godoc
// @Summary      Log in
// @Tags         auth
// @Accept       json
// @Produce      json
// @Param        body  body  loginRequest  true  "Email and password"
// @Success      200   {object}  tokenResponse
// @Failure      401   {object}  errBody
// @Router       /auth/login [post]
func (h *Handler) Login(c *gin.Context) {
	var req loginRequest
	if err := c.ShouldBindJSON(&req); err != nil {
		c.JSON(http.StatusBadRequest, errResp("invalid request body"))
		return
	}

	res, err := h.svc.Login(c.Request.Context(), service.LoginCmd{
		Email:    req.Email,
		Password: req.Password,
	})
	if err != nil {
		h.handleErr(c, err)
		return
	}

	c.JSON(http.StatusOK, tokenResponse{
		Token:     res.Token,
		ExpiresAt: res.ExpiresAt.Format("2006-01-02T15:04:05Z07:00"),
	})
}

// Me godoc
// @Summary      Get current user
// @Tags         auth
// @Security     BearerAuth
// @Produce      json
// @Success      200  {object}  userResponse
// @Failure      401  {object}  errBody
// @Router       /auth/me [get]
func (h *Handler) Me(c *gin.Context) {
	claims := middleware.GetClaims(c)
	if claims == nil {
		c.JSON(http.StatusUnauthorized, errResp("unauthorized"))
		return
	}
	c.JSON(http.StatusOK, userResponse{
		ID:    claims.UserID,
		Email: claims.Email,
	})
}

// handleErr maps domain errors to HTTP codes.
func (h *Handler) handleErr(c *gin.Context, err error) {
	switch {
	case errors.Is(err, domain.ErrInvalidEmail),
		errors.Is(err, domain.ErrPasswordTooShort):
		c.JSON(http.StatusBadRequest, errResp(err.Error()))
	case errors.Is(err, domain.ErrEmailAlreadyTaken):
		c.JSON(http.StatusConflict, errResp(err.Error()))
	case errors.Is(err, domain.ErrInvalidCredentials):
		c.JSON(http.StatusUnauthorized, errResp(err.Error()))
	case errors.Is(err, domain.ErrUserNotFound):
		c.JSON(http.StatusNotFound, errResp(err.Error()))
	default:
		h.logger.ErrorContext(c.Request.Context(), "unexpected handler error", "error", err)
		c.JSON(http.StatusInternalServerError, errResp("internal server error"))
	}
}

// mustUserID extracts and parses the user ID from JWT claims.
func mustUserID(c *gin.Context) (uuid.UUID, bool) {
	claims := middleware.GetClaims(c)
	if claims == nil {
		c.JSON(http.StatusUnauthorized, errResp("unauthorized"))
		return uuid.Nil, false
	}
	id, err := uuid.Parse(claims.UserID)
	if err != nil {
		c.JSON(http.StatusUnauthorized, errResp("invalid user id in token"))
		return uuid.Nil, false
	}
	return id, true
}
