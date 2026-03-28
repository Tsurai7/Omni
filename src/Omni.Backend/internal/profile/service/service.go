// Package service contains the profile/auth business logic.
package service

import (
	"context"
	"log/slog"
	"regexp"
	"strings"
	"time"

	"omni-backend/internal/auth"
	"omni-backend/internal/profile/domain"

	"github.com/google/uuid"
)

var emailRegex = regexp.MustCompile(`^[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}$`)

const minPasswordLen = 8

// AuthService is the application use-case interface for auth/profile.
type AuthService interface {
	Register(ctx context.Context, cmd RegisterCmd) (*RegisterResult, error)
	Login(ctx context.Context, cmd LoginCmd) (*TokenResult, error)
	GetUser(ctx context.Context, userID uuid.UUID) (*domain.User, error)
}

// RegisterCmd is the input value object for user registration.
type RegisterCmd struct {
	Email    string
	Password string
}

// LoginCmd is the input value object for user login.
type LoginCmd struct {
	Email    string
	Password string
}

// RegisterResult carries the newly created user + issued JWT.
type RegisterResult struct {
	User      *domain.User
	Token     string
	ExpiresAt time.Time
}

// TokenResult carries a freshly issued JWT.
type TokenResult struct {
	Token     string
	ExpiresAt time.Time
}

type authService struct {
	repo      domain.UserRepository
	jwtSecret string
	jwtExpiry time.Duration
	logger    *slog.Logger
}

// New returns an AuthService wired to the given repository.
func New(repo domain.UserRepository, jwtSecret string, jwtExpiry time.Duration, logger *slog.Logger) AuthService {
	return &authService{
		repo:      repo,
		jwtSecret: jwtSecret,
		jwtExpiry: jwtExpiry,
		logger:    logger,
	}
}

func (s *authService) Register(ctx context.Context, cmd RegisterCmd) (*RegisterResult, error) {
	email := strings.TrimSpace(strings.ToLower(cmd.Email))
	if !emailRegex.MatchString(email) {
		return nil, domain.ErrInvalidEmail
	}
	if len(cmd.Password) < minPasswordLen {
		return nil, domain.ErrPasswordTooShort
	}

	hash, err := auth.HashPassword(cmd.Password)
	if err != nil {
		s.logger.ErrorContext(ctx, "failed to hash password", "error", err)
		return nil, err
	}

	user := &domain.User{
		ID:           uuid.New(),
		Email:        email,
		PasswordHash: hash,
		CreatedAt:    time.Now().UTC(),
	}

	if err := s.repo.Create(ctx, user); err != nil {
		return nil, err
	}

	token, expiresAt, err := auth.GenerateToken(user.ID, user.Email, s.jwtSecret, s.jwtExpiry)
	if err != nil {
		s.logger.ErrorContext(ctx, "failed to generate token after registration", "user_id", user.ID, "error", err)
		return nil, err
	}

	s.logger.InfoContext(ctx, "user registered", "user_id", user.ID, "email", user.Email)
	return &RegisterResult{User: user, Token: token, ExpiresAt: expiresAt}, nil
}

func (s *authService) Login(ctx context.Context, cmd LoginCmd) (*TokenResult, error) {
	email := strings.TrimSpace(strings.ToLower(cmd.Email))

	user, err := s.repo.GetByEmail(ctx, email)
	if err != nil {
		// Map "not found" to same error as "wrong password" to prevent user enumeration.
		return nil, domain.ErrInvalidCredentials
	}

	if err := auth.ComparePassword(user.PasswordHash, cmd.Password); err != nil {
		s.logger.WarnContext(ctx, "login failed: wrong password", "email", email)
		return nil, domain.ErrInvalidCredentials
	}

	token, expiresAt, err := auth.GenerateToken(user.ID, user.Email, s.jwtSecret, s.jwtExpiry)
	if err != nil {
		s.logger.ErrorContext(ctx, "failed to generate token after login", "user_id", user.ID, "error", err)
		return nil, err
	}

	s.logger.InfoContext(ctx, "user logged in", "user_id", user.ID, "email", email)
	return &TokenResult{Token: token, ExpiresAt: expiresAt}, nil
}

func (s *authService) GetUser(ctx context.Context, userID uuid.UUID) (*domain.User, error) {
	return s.repo.GetByID(ctx, userID)
}
