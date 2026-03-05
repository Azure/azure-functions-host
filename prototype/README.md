# Worker Model Prototype

This prototype demonstrates the decoupled Runtime/Worker architecture for Azure Functions.

## Project Structure

```
prototype/
├── WorkerModelPrototype.sln          # Solution file
├── WorkerModel.Contracts/            # Shared types and gRPC protos
│   ├── Protos/
│   │   ├── sidecar_rpc.proto         # Worker ↔ Sidecar gRPC contract
│   │   └── wrapper_control.proto     # Sidecar ↔ Wrapper control contract
│   ├── ApplicationDefinition.cs      # App definition record
│   ├── HostAssignmentContext.cs      # SC assignment payload
│   └── WorkerContext.cs              # Worker context record
├── WorkerModel.Sidecar/              # gRPC proxy between Worker and Runtime
│   ├── Controllers/
│   │   ├── AssignController.cs       # POST /assign endpoint for SC
│   │   └── HealthController.cs       # Health check endpoints
│   └── Services/
│       ├── RuntimeConnectionManager.cs   # Manages gRPC connection to Runtime
│       ├── SidecarRpcService.cs          # gRPC service for FunctionsNetHost
│       ├── SpecializationService.cs      # Handles /assign flow
│       └── WorkerState.cs                # Tracks worker state
└── WorkerModel.Wrapper/              # Process supervisor for FunctionsNetHost
    ├── Program.cs                    # Entry point
    ├── WrapperConfig.cs              # Configuration from env vars
    └── WorkerProcessManager.cs       # Manages FunctionsNetHost process
```

## Week 1 Implementation

### ✅ Create project structure
- Solution with 3 projects: Contracts, Sidecar, Wrapper
- Disabled central package management for prototype isolation

### ✅ Define proto contracts
- `sidecar_rpc.proto`: gRPC contract for FunctionsNetHost ↔ Sidecar communication
- `wrapper_control.proto`: gRPC contract for Sidecar ↔ Wrapper control

### ✅ Implement basic Wrapper
- Process supervisor for FunctionsNetHost
- Starts worker with correct arguments
- Forwards signals (SIGTERM/SIGINT)
- Monitors worker exit

### ✅ Implement basic Sidecar
- gRPC service for FunctionsNetHost to connect to
- HTTP endpoints for health checks and /assign
- Placeholder mode: accepts connections but doesn't forward to Runtime
- Specialized mode: proxies messages to Runtime

## Building

```powershell
cd azure-functions-host/prototype
dotnet build WorkerModelPrototype.sln
```

## Running

### Sidecar
```powershell
cd WorkerModel.Sidecar
dotnet run
# Listens on:
#   HTTP: http://localhost:8080 (health + /assign)
#   gRPC: http://localhost:50051 (worker connection)
```

### Wrapper
```powershell
cd WorkerModel.Wrapper
# Set environment variables
$env:WORKER_ID = "worker-001"
$env:FUNCTIONS_URI = "http://localhost:50051"
$env:FUNCTIONS_NETHOST_PATH = "path/to/FunctionsNetHost"
dotnet run
```

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    Worker Container                          │
│  ┌─────────────┐     ┌─────────────┐     ┌───────────────┐  │
│  │   Wrapper   │────▶│   Sidecar   │────▶│FunctionsNetHost│  │
│  │   (PID 1)   │     │  (gRPC+HTTP)│◀────│   (Worker)    │  │
│  └─────────────┘     └──────┬──────┘     └───────────────┘  │
│                             │                                │
└─────────────────────────────┼───────────────────────────────┘
                              │ gRPC (after /assign)
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                    Runtime Container                         │
│                    (WebJobs.Script.WebHost)                  │
└─────────────────────────────────────────────────────────────┘
```

## Week 2 Implementation

### ✅ Connect Sidecar to existing WebHost
- Generated gRPC types from FunctionRpc.proto (same types used by WebHost)
- SidecarRpcService implements FunctionRpc.FunctionRpcBase for FunctionsNetHost compatibility
- RuntimeConnectionManager proxies messages to Runtime's gRPC endpoint

### ✅ Implement Wrapper restart API
- Wrapper control gRPC service for health checks and shutdown
- WrapperControlService exposes WorkerStatus, GracefulShutdown, RestartWorker RPCs

### ✅ Create Docker images
- `docker/Dockerfile.sidecar`: Alpine-based Sidecar image
- `docker/Dockerfile.wrapper`: Alpine-based Wrapper image
- Docker Compose configuration for local testing

## Week 3 Implementation

### ✅ Scale Controller with In-Memory + Blob storage
- WorkerModel.ScaleController project with ASP.NET Core WebAPI
- In-memory storage for: applications, workers, runtimes (prototype simplicity)
- Blob container: `function-apps` for app zip packages
- Full CRUD operations for apps, workers, and runtimes
- SpecializationOrchestrator implements late-binding flow:
  1. Find available placeholder Runtime
  2. Find available placeholder Worker
  3. Generate SAS URL for app package
  4. Call Runtime `/admin/instance/assign`
  5. Call Worker Sidecar `/assign` with RuntimeEndpoint
- Web UI at `/index.html` for deployment and monitoring

### ✅ Aspire orchestration
- WorkerModel.AppHost project with Aspire orchestration
- WorkerModel.ServiceDefaults for common configuration
- Starts:
  - Cosmos DB emulator
  - Azure Blob Storage emulator
  - Scale Controller (port 5200)
  - Sidecar instances (ports 5301/50051, 5302/50052)
- Automatic worker registration on Sidecar startup

### 🔄 Zip upload and download flow
- ApplicationService.DeployAsync uploads zip to Blob storage
- ApplicationService.GetDownloadUrlAsync generates SAS token
- Zip extraction/mounting pending (see Week 4 plan)

### ⏳ HTTP trigger invocation
- Requires implementing reverse proxy in ScaleController (see Week 4 plan)
- ScaleController will be entry point for HTTP requests
- Request triggers specialization on cold start

## Building

```powershell
cd azure-functions-host/prototype
dotnet build WorkerModelPrototype.sln
```

## Running with Aspire

```powershell
cd WorkerModel.AppHost
dotnet run
# Opens Aspire dashboard showing all services
# Scale Controller: http://localhost:5200
# Swagger: http://localhost:5200/swagger
```

## Running Individually

### Scale Controller
```powershell
cd WorkerModel.ScaleController
dotnet run
# Web UI: http://localhost:5200
# API: http://localhost:5200/swagger
```

### Sidecar
```powershell
cd WorkerModel.Sidecar
$env:SIDECAR_WORKER_ID = "worker-001"
$env:SCALE_CONTROLLER_ENDPOINT = "http://localhost:5200"
dotnet run
# HTTP: http://localhost:8080
# gRPC: http://localhost:50051
```

### Wrapper
```powershell
cd WorkerModel.Wrapper
$env:WORKER_ID = "worker-001"
$env:FUNCTIONS_URI = "http://localhost:50051"
$env:FUNCTIONS_NETHOST_PATH = "path/to/FunctionsNetHost"
dotnet run
```

## Architecture

```
                         HTTP Requests
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                    Scale Controller                          │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────┐  │
│  │ In-Memory   │  │ Blob Storage│  │ Reverse Proxy +     │  │
│  │  (metadata) │  │  (packages) │  │ Specialization      │  │
│  └─────────────┘  └─────────────┘  └─────────────────────┘  │
└─────────────────────────────────────┬───────────────────────┘
                                      │
          ┌───────────────────────────┼───────────────────────┐
          │ /admin/instance/assign    │ /assign               │
          ▼                           ▼                       │
┌─────────────────────────┐  ┌────────────────────────────────┴┐
│   Runtime Container     │  │        Worker Container          │
│   (WebJobs.Script.      │  │  ┌─────────┐  ┌───────────────┐ │
│    WebHost)             │◀─┼──│ Sidecar │◀─│FunctionsNetHost│ │
│                         │  │  └────┬────┘  └───────────────┘ │
│                         │  │       │ gRPC                     │
└─────────────────────────┘  └───────┼────────────────────────┘
          ▲                          │
          └──────────────────────────┘
            gRPC (function invocations)
```

**Request Flow:**
1. HTTP request arrives at ScaleController
2. ScaleController routes to appropriate app
3. If cold start: specialize Runtime + Worker, mount code
4. Forward request to Runtime, which invokes Worker via gRPC
5. Response flows back through ScaleController to client

## Next Steps (Week 4)

### 🔄 Request-Triggered Specialization Flow

The specialization flow should be triggered by actual HTTP requests to the application, with the ScaleController acting as the entry point and reverse proxy:

```
┌─────────────────────────────────────────────────────────────────┐
│                    HTTP Request Flow                             │
│                                                                  │
│  Client ──▶ ScaleController ──▶ Runtime ──▶ Worker              │
│             (reverse proxy)                                      │
└─────────────────────────────────────────────────────────────────┘

URL Format:
  Client request:  GET /{appId}/api/hello?name=World
  Runtime request: GET /api/hello?name=World

Cold Start Sequence:
1. Client sends HTTP request to ScaleController (e.g., GET /customer/api/hello)
2. ScaleController extracts appId from URL path ("customer")
3. ScaleController looks up app metadata by appId
4. If no specialized Runtime+Worker for this app:
   a. Pick an eligible placeholder Runtime
   b. Pick an eligible placeholder Worker
   c. Mount the zip file from blob storage
      - Linux: SquashFS mount
      - Windows: Unzip to temp folder
   d. Send /admin/instance/assign to Runtime with env vars + script root
   e. Send /assign to Worker Sidecar with Runtime endpoint
   f. Wait for both to be ready
5. Forward the request to Runtime (strip appId prefix: /api/hello?name=World)
6. Return response to client
7. Record cold start timing metrics
```

**Key Design Points:**
- ScaleController is the single entry point for all function app HTTP traffic
- Specialization happens on-demand when first request arrives (true cold start)
- The original request is queued and forwarded after specialization completes
- Timing metrics captured: total cold start time, mount time, assign time, first response time

**Implementation Tasks:**
- [ ] Add reverse proxy endpoint to ScaleController: `/{appId}/{**path}`
- [ ] Strip appId prefix when forwarding to Runtime
- [ ] Implement request queuing during specialization
- [ ] Add zip extraction for Windows (SquashFS equivalent)
- [ ] Update RuntimeSidecar to handle zip mount path
- [ ] Add timing/metrics collection
- [ ] Handle concurrent requests during cold start (queue multiple, specialize once)

### Other Week 4 Tasks
- [ ] End-to-end HTTP trigger test with real WebHost
- [ ] Config change detection
- [ ] Worker restart flow for updates
- [ ] Error handling and retry logic
- [ ] Documentation and demo video
