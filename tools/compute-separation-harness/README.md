# Compute Separation Harness

End-to-end demo harness for Azure Functions **compute separation** (a.k.a. external worker mode). It wires together three processes so you can observe gRPC message flow between the Functions Runtime and a Language Worker through an intermediary Worker Proxy.

## Architecture

```
┌────────────────┐  gRPC (50051)  ┌──────────────┐  gRPC (50052)  ┌────────────────┐
│ Functions Host │◄──────────────►│ Worker Proxy │◄──────────────►│ Language Worker │
│ (Runtime)      │                │ (Relay)      │                │ (e.g. Node.js) │
└────────────────┘                └──────────────┘                └────────────────┘
     :7071 HTTP                    :50053 HTTP ──► :8080 Worker HTTP
```

| Component | Description |
|-----------|-------------|
| **Runtime** | `src/WebJobs.Script.WebHost` — the Azure Functions host, started with `FUNCTIONS_WORKER_EXTERNAL_ENABLED=true` so it connects to the worker proxy instead of launching a worker in-process. |
| **Worker Proxy** | `src/Functions.WorkerProxy` — a gRPC relay that sits between the runtime and the worker, forwarding `StreamingMessage` payloads in both directions. Also runs a YARP reverse-proxy for HTTP traffic. |
| **Worker** | Any Azure Functions language worker (Node.js, Python, etc.) pointed at the worker proxy's worker gRPC port. |

## Prerequisites

| Prerequisite | Version |
|---|---|
| .NET SDK | 8.0 (see `global.json`) |
| Node.js | 18+ *(only if using a Node.js worker)* |
| Aspire workload | *(only for the AppHost project — optional)* |

## Quick Start (Full E2E)

The fastest way to see the full flow end-to-end using the **MockWorker** (no external language runtime required):

### Terminal 1: Start the worker proxy

```powershell
cd src/Functions.WorkerProxy
dotnet run
```

### Terminal 2: Start the mock worker

```powershell
cd tools/compute-separation-harness/MockWorker
dotnet run
```

### Terminal 3: Start the runtime

```powershell
$env:FUNCTIONS_WORKER_EXTERNAL_ENABLED = "true"
$env:FUNCTIONS_WORKER_EXTERNAL_GRPC_ENDPOINT = "http://localhost:50051"
$env:AzureWebJobsScriptRoot = "C:\some\empty\dir"
dotnet run --project src/WebJobs.Script.WebHost
```

### Terminal 4: Test it

```bash
curl http://localhost:7071/api/HttpTrigger
# Expected: Hello from mock worker!
```

## Quick Start — PowerShell Script

The PowerShell script automates the above steps. It does **not** require Aspire.

```powershell
# From the repo root (starts Worker Proxy + MockWorker + Runtime):
.\tools\compute-separation-harness\scripts\start.ps1

# With a real language worker instead of the mock:
.\tools\compute-separation-harness\scripts\start.ps1 -NoMockWorker

# With a sample function app:
.\tools\compute-separation-harness\scripts\start.ps1 -SampleApp .\sample\MyFunctionApp

# Override ports:
.\tools\compute-separation-harness\scripts\start.ps1 -RuntimeGrpcPort 60051 -RuntimePort 8071
```

The script:
1. Builds the Worker Proxy, MockWorker, and Runtime.
2. Starts the Worker Proxy (gRPC relay + HTTP proxy).
3. Starts the MockWorker (unless `-NoMockWorker` is specified).
4. Starts the Runtime in external-worker mode, connected to the worker proxy.
5. Prints instructions for testing.

### Attaching a Real Language Worker

If you pass `-NoMockWorker`, start your worker separately, pointing it at the worker proxy's **worker gRPC port** (default `50052`):

```bash
# Node.js worker example:
export FUNCTIONS_GRPC_HOST=127.0.0.1
export FUNCTIONS_GRPC_PORT=50052
node <path-to-worker>/dist/src/worker-bundle.js
```

### Testing

Once a worker is attached and has responded to `WorkerInitRequest` / `FunctionMetadataRequest`:

```bash
curl http://localhost:7071/api/HttpTrigger
```

## Aspire AppHost (Optional)

The `AppHost/` project provides an Aspire-based orchestration alternative. It requires the [.NET Aspire workload](https://learn.microsoft.com/dotnet/aspire/fundamentals/setup-tooling).

```bash
# Install the Aspire workload (one-time):
dotnet workload install aspire

# Run the AppHost:
dotnet run --project tools/compute-separation-harness/AppHost/AppHost.csproj
```

> **Note**: The AppHost project is best-effort. If the Aspire packages fail to restore, use the PowerShell script instead.

## Port Reference

| Port | Protocol | Default | Description |
|------|----------|---------|-------------|
| Runtime gRPC | HTTP/2 | 50051 | Runtime ↔ Worker Proxy |
| Worker gRPC  | HTTP/2 | 50052 | Worker ↔ Worker Proxy |
| HTTP Proxy   | HTTP/1 | 50053 | Worker Proxy → Worker HTTP endpoint |
| Runtime HTTP | HTTP/1 | 7071  | Functions host HTTP trigger endpoint |

## Environment Variables

| Variable | Value | Set On |
|----------|-------|--------|
| `FUNCTIONS_WORKER_EXTERNAL_ENABLED` | `true` | Runtime |
| `FUNCTIONS_WORKER_EXTERNAL_GRPC_ENDPOINT` | `http://localhost:50051` | Runtime |
| `FUNCTIONS_GRPC_HOST` | `127.0.0.1` | Worker |
| `FUNCTIONS_GRPC_PORT` | `50052` | Worker |

## Known Limitations

- The MockWorker is a minimal stub and only supports a single hardcoded `HttpTrigger` function. Use a real language worker for anything beyond basic smoke testing.
- The Aspire AppHost project requires the Aspire workload, which may not be installed in all environments.
- The PowerShell script uses `Start-Process` and may not capture worker stdout/stderr inline. Use the Aspire AppHost for integrated log viewing.
- Windows only for the PowerShell script (use Aspire AppHost for cross-platform support).
