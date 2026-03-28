.PHONY: build test test-unit test-integration test-all cover lint

# Build all services
build:
	go build ./...

# Unit tests only (no testcontainers, no DB required)
test-unit:
	go test -race ./...

# Integration tests (requires Docker; uses testcontainers)
test-integration:
	go test -race -tags integration ./...

# Run both unit and integration tests
test-all: test-unit test-integration

# Coverage report (unit tests)
cover:
	go test -race -coverprofile=coverage.out ./...
	go tool cover -html=coverage.out -o coverage.html
	@echo "Coverage report: coverage.html"

# Lint (requires golangci-lint)
lint:
	golangci-lint run ./...
