# syntax=docker/dockerfile:1
# Build stage: build all service binaries
FROM golang:1.26-alpine AS builder
WORKDIR /app

COPY go.mod go.sum ./
RUN --mount=type=cache,target=/go/pkg/mod \
    go mod download

COPY . .
RUN --mount=type=cache,target=/root/.cache/go-build \
    --mount=type=cache,target=/go/pkg/mod \
    CGO_ENABLED=0 go build -o /gateway ./cmd/gateway && \
    CGO_ENABLED=0 go build -o /profile ./cmd/profile && \
    CGO_ENABLED=0 go build -o /task ./cmd/task && \
    CGO_ENABLED=0 go build -o /telemetry ./cmd/telemetry && \
    CGO_ENABLED=0 go build -o /telemetry-consumer ./cmd/telemetry-consumer && \
    CGO_ENABLED=0 go build -o /calendar ./cmd/calendar

# Run stage: single image with all binaries (which one runs is set by CMD)
FROM alpine:3.21
RUN apk --no-cache add ca-certificates
WORKDIR /app
COPY --from=builder /gateway /profile /task /telemetry /telemetry-consumer /calendar .
EXPOSE 8080 8081 8082 8083 8084
# Default for docker run without command (e.g. docker-compose override)
CMD ["./gateway"]
