# Build stage: build all service binaries
FROM golang:1.26-alpine AS builder
WORKDIR /app

COPY go.mod go.sum ./
RUN go mod download

COPY . .
RUN CGO_ENABLED=0 go build -o /gateway ./cmd/gateway && \
    CGO_ENABLED=0 go build -o /profile ./cmd/profile && \
    CGO_ENABLED=0 go build -o /task ./cmd/task && \
    CGO_ENABLED=0 go build -o /telemetry ./cmd/telemetry && \
    CGO_ENABLED=0 go build -o /telemetry-consumer ./cmd/telemetry-consumer

# Run stage: single image with all binaries (which one runs is set by CMD)
FROM alpine:3.19
RUN apk --no-cache add ca-certificates
WORKDIR /app
COPY --from=builder /gateway /profile /task /telemetry /telemetry-consumer .
EXPOSE 8080 8081 8082 8083
# Default for docker run without command (e.g. docker-compose override)
CMD ["./gateway"]
