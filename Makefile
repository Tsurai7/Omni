BACKEND_DIR := src/Omni.Backend
CLIENT_DIR  := src/Omni.Client


COMPOSE_LOCAL := docker compose \
	-f docker-compose.yml \
	--env-file .env.local

COMPOSE_PROD := docker compose \
	-f docker-compose.production.yml \
	--env-file .env.prod

.PHONY: lint local prod stop logs build-macos build-windows

## Build client for macOS (Mac Catalyst) in Release and package as DMG
build-macos:
	cd $(CLIENT_DIR) && dotnet publish Omni.Client.csproj \
		-f net10.0-maccatalyst \
		-c Release \
		-p:TargetFrameworks=net10.0-maccatalyst \
		-p:SkipMacCodesign=true \
		-p:CodesignDisable=true
	cd $(CLIENT_DIR) && APP=$$(find bin/Release/net10.0-maccatalyst -name "*.app" -maxdepth 4 | head -1) && \
		echo "Packaging: $$APP" && \
		hdiutil create -volname "Omni" -srcfolder "$$APP" -ov -format UDZO Omni-macos.dmg

## Build client for Windows (x64) in Release as a self-contained exe
build-windows:
	cd $(CLIENT_DIR) && dotnet publish Omni.Client.csproj \
		-f net10.0-windows10.0.19041.0 \
		-c Release \
		-r win-x64 \
		--self-contained true \
		-p:PublishSingleFile=true \
		-p:TargetFrameworks=net10.0-windows10.0.19041.0 \
		-p:EnableWindowsTargeting=true

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
