# syntax=docker/dockerfile:1
FROM golang:1.26-alpine AS builder
WORKDIR /app

COPY go.mod go.sum ./
RUN --mount=type=cache,target=/go/pkg/mod \
    go mod download

COPY . .
RUN --mount=type=cache,target=/root/.cache/go-build \
    --mount=type=cache,target=/go/pkg/mod \
    CGO_ENABLED=0 go build -o /profile ./cmd/profile

FROM alpine:3.21
RUN apk --no-cache add ca-certificates wget
WORKDIR /app
COPY --from=builder /profile .
EXPOSE 8081
CMD ["./profile"]
