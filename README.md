# SmartCity Monitoring

Cameras on Konya municipality vehicles stream frames and telemetry to the cloud.
A cloud AI detects road and infrastructure problems (potholes, damaged assets,
illegal signs, unsafe construction). Detections are stored, costed, turned into
work orders, and dispatched to crews.

This repository contains the **walking skeleton**: every service exists and the
whole chain runs end to end, but most internals are deliberately simple stubs.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Docker](https://docs.docker.com/get-docker/) and Docker Compose

## Quick Start

### 1. Start infrastructure

```bash
cp .env.example .env          # first time only
docker compose up -d
docker compose ps              # wait until all containers show "healthy"
```

Infrastructure containers: PostgreSQL + PostGIS, RabbitMQ, EMQX (MQTT broker),
SeaweedFS (S3-compatible blob storage), Redis, Seq (structured logging).

### 2a. Run services locally (development)

```bash
dotnet build SmartCity.sln
bash scripts/run-all.sh        # starts all services in the background
bash scripts/stop-all.sh       # stops them
```

Logs are written to `.run/logs/`.

### 2b. Run services in Docker

```bash
docker compose --profile apps up -d --build
```

This builds and starts all 10 application services alongside the infrastructure.
The `apps` profile keeps them separate so `docker compose up -d` (without
`--profile`) starts only infrastructure.

### 3. Run the vehicle simulator

```bash
dotnet run --project tools/VehicleSimulator -- --vehicles 5 --interval-ms 2000
```

The simulator publishes camera frames over MQTT. With `FakeAi:ForceDetection`
enabled on AiDetection, every frame triggers a detection through the full
pipeline: pothole saved, cost estimated, work order created, notification sent.

### 4. Run tests

```bash
# Unit / integration tests
dotnet test SmartCity.sln

# End-to-end test (requires running stack — local or containerized)
dotnet test tests/SmartCity.EndToEnd.Tests
```

The E2E test publishes one MQTT frame and verifies the full chain: frame
ingestion, pothole detection, cost estimation, and work order creation with a
matching estimated cost.

When running against the containerized stack, AiDetection already has
`FakeAi__ForceDetection=true` set. For local development, set it manually:

```bash
FakeAi__ForceDetection=true dotnet run --project src/Services/AiDetection/SmartCity.AiDetection
```

### 5. Follow a trace in Seq

Open [http://localhost:8081](http://localhost:8081) in your browser.

Every frame gets a unique `CorrelationId` (the frame's GUID). To follow a single
frame through all services:

1. Find a log entry from any service (e.g., `VehicleCommunication` publishing
   `FrameUploadedToStorage`)
2. Click the `CorrelationId` property value
3. Filter by that value — Seq shows every log line from every service that
   handled that frame, in order

This traces the full path: MQTT ingestion, S3 upload, AI detection, pothole
save, cost calculation, work order creation, and notification.

## Architecture

```
Browser/App ─── HTTP/REST ──→ API Gateway ─── gRPC ──→ Services
                                                         │
MQTT (vehicles) ──→ VehicleCommunication ──→ RabbitMQ ──→┘
```

- **Browser to Gateway**: HTTP/REST
- **Gateway to services**: gRPC
- **Service to service**: RabbitMQ events only (no direct gRPC between services)
- **Database per service**: each service owns its own PostgreSQL database

## Services

| Service | HTTP | gRPC | Description |
|---|---|---|---|
| ApiGateway | 5000 | – | REST API gateway, proxies to services via gRPC |
| VehicleCommunication | 5210 | – | MQTT subscriber, uploads frames to S3 |
| AiDetection | 5220 | – | Consumes frames, runs fake AI, publishes detections |
| Pothole | 5230 | 5231 | Stores road damage records |
| Inventory | 5240 | 5241 | Tracks municipal asset damage |
| Violation | 5250 | 5251 | Manages regulation violations |
| Building | 5260 | 5261 | Monitors unsafe construction sites |
| CostCalculation | 5270 | 5271 | Estimates repair costs |
| WorkOrder | 5280 | 5281 | Creates and manages work orders |
| Notification | 5290 | 5291 | Sends notifications to officers |

## Infrastructure UIs

| Service | URL | Credentials |
|---|---|---|
| RabbitMQ | http://localhost:15672 | smartcity / smartcity |
| EMQX | http://localhost:18083 | admin / public |
| Seq | http://localhost:8081 | (no auth) |

## Reset all data

```bash
docker compose --profile apps down -v
docker compose up -d           # restart clean infrastructure
```
