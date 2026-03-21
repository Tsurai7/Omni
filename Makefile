BACKEND_DIR := src/Omni.Backend

COMPOSE_LOCAL := docker compose \
	-f docker-compose.yml \
	--env-file .env.local

COMPOSE_PROD := docker compose \
	-f docker-compose.production.yml \
	--env-file .env.prod

.PHONY: lint local prod stop logs

## Lint backend Go services
lint:
	cd $(BACKEND_DIR) && golangci-lint run ./...

## Run full stack locally using .env.local
local:
	$(COMPOSE_LOCAL) up -d

## Run production stack using .env.prod
prod:
	$(COMPOSE_PROD) up -d

## Stop all running containers
stop:
	docker compose down

## Tail logs (usage: make logs s=gateway)
logs:
	docker compose logs -f $(s)
