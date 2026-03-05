var builder = DistributedApplication.CreateBuilder(args);

// Infrastructure - Azure Blob Storage emulator for app packages (zip files)
var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator();

var blobs = storage.AddBlobs("blobs");

// Scale Controller - orchestrates workers and runtimes (in-memory for metadata, blobs for packages)
var scaleController = builder.AddProject("scalecontroller", "../WorkerModel.ScaleController/WorkerModel.ScaleController.csproj")
    .WithReference(blobs)
    .WaitFor(storage)
    .WithExternalHttpEndpoints();

// Runtime Sidecar - handles SquashFS mounting for the Runtime pod
// Needs runtime reference to know WebHost endpoint for SC registration
var runtimeSidecar = builder.AddProject("runtime-sidecar", "../WorkerModel.RuntimeSidecar/WorkerModel.RuntimeSidecar.csproj")
    .WithReference(scaleController)
    .WithReference(blobs)
    .WaitFor(scaleController)
    .WaitFor(storage)
    .WithEnvironment("RUNTIME_SIDECAR_ID", "runtime-1");

// Runtime (existing WebHost) - runs as placeholder, specialized by SC
// Use fixed port 7071 for predictable WebHost HTTP endpoint 
// gRPC server runs internally on port 7072 (set via FUNCTIONS_GRPC_PORT)
var runtime = builder.AddProject("runtime", "../../src/WebJobs.Script.WebHost/WebJobs.Script.WebHost.csproj")
    .WithHttpEndpoint(name: "webhost", port: 7071, isProxied: false)  // Fixed port for WebHost HTTP
    .WithReference(scaleController)
    .WithReference(runtimeSidecar)
    .WaitFor(scaleController)
    .WaitFor(runtimeSidecar)
    .WithEnvironment("WEBSITE_PLACEHOLDER_MODE", "1") // Host starts in placeholder mode, waits for specialization
    .WithEnvironment("WorkerModel__DecoupledMode", "true")
    .WithEnvironment("WorkerModel__DisableProcessManagement", "true")
    .WithEnvironment("AzureWebJobsScriptRoot", "/home/site/wwwroot")
    .WithEnvironment("FUNCTIONS_WORKER_RUNTIME", "dotnet-isolated")
    .WithEnvironment("FUNCTIONS_GRPC_PORT", "7072");

// RuntimeSidecar needs to know WebHost endpoint - add reference AFTER runtime is defined
runtimeSidecar.WithReference(runtime);

// Add runtime reference to ScaleController so it can forward requests via service discovery
scaleController.WithReference(runtime);

// Worker Sidecar instance 1 - manages Worker 1's gRPC bridge to Runtime
// HTTP endpoint is proxied by Aspire (service discovery, health checks)
// gRPC endpoint is non-proxied (FunctionsNetHost connects directly via HTTP/2)
var sidecar1 = builder.AddProject("worker-sidecar1", "../WorkerModel.Sidecar/WorkerModel.Sidecar.csproj")
    .WithHttpEndpoint(name: "grpc", port: 5086, isProxied: false)
    .WithReference(scaleController)
    .WaitFor(scaleController)
    .WithEnvironment("SIDECAR_WORKER_ID", "worker-1")
    .WithEnvironment("SIDECAR_GRPC_PORT", "5086");

// Worker Sidecar instance 2 - manages Worker 2's gRPC bridge (scale-out)
var sidecar2 = builder.AddProject("worker-sidecar2", "../WorkerModel.Sidecar/WorkerModel.Sidecar.csproj")
    .WithHttpEndpoint(name: "grpc", port: 5087, isProxied: false)
    .WithReference(scaleController)
    .WaitFor(scaleController)
    .WithEnvironment("SIDECAR_WORKER_ID", "worker-2")
    .WithEnvironment("SIDECAR_GRPC_PORT", "5087");

// FunctionsNetHost binary (from NuGet package, pre-built)
// In production, this binary is the container entrypoint (PID 1).
// In Aspire, we launch it directly via AddExecutable.
var functionsNetHostPath = Path.GetFullPath(
    Path.Combine(builder.AppHostDirectory, "..", "WorkerModel.Wrapper", "bin", "Debug", "net8.0",
        "workers", "dotnet-isolated", "bin", "FunctionsNetHost.exe"));

// Worker 1 - FunctionsNetHost (PID 1 in its own container)
// FunctionsNetHost arg parser does args.Skip(1), so first arg is a dummy.
var worker1 = builder.AddExecutable("worker1", functionsNetHostPath, builder.AppHostDirectory,
        functionsNetHostPath, // dummy arg[0] — FunctionsNetHost skips it
        "--functions-uri", "http://localhost:5086",
        "--functions-worker-id", "worker-1",
        "--functions-request-id", Guid.NewGuid().ToString(),
        "--functions-grpc-max-message-length", "134217728")
    .WaitFor(sidecar1)
    .WithEnvironment("FUNCTIONS_WORKER_RUNTIME_VERSION", "8.0")
    .WithEnvironment("AZURE_FUNCTIONS_FUNCTIONSNETHOST_TRACE", "1");

// Worker 2 - FunctionsNetHost (scale-out, same binary)
var worker2 = builder.AddExecutable("worker2", functionsNetHostPath, builder.AppHostDirectory,
        functionsNetHostPath,
        "--functions-uri", "http://localhost:5087",
        "--functions-worker-id", "worker-2",
        "--functions-request-id", Guid.NewGuid().ToString(),
        "--functions-grpc-max-message-length", "134217728")
    .WaitFor(sidecar2)
    .WithEnvironment("FUNCTIONS_WORKER_RUNTIME_VERSION", "8.0")
    .WithEnvironment("AZURE_FUNCTIONS_FUNCTIONSNETHOST_TRACE", "1");

// Pod topology (what this models):
//
//   Worker Pod 1                    Worker Pod 2
//   ┌──────────────────┐           ┌──────────────────┐
//   │ Sidecar 1        │           │ Sidecar 2        │
//   │ (gRPC :5086)     │           │ (gRPC :5087)     │
//   ├──────────────────┤           ├──────────────────┤
//   │ FunctionsNetHost │           │ FunctionsNetHost │
//   │ (PID 1)          │           │ (PID 1)          │
//   └──────────────────┘           └──────────────────┘
//   Shared localhost network       Shared localhost network

// Startup flow:
// 1. Aspire starts Storage emulator
// 2. ScaleController starts (in-memory metadata, blobs for packages)
// 3. RuntimeSidecar + Runtime start (wait for SC)
// 4. Worker Sidecars 1 & 2 start (wait for SC), register as placeholders
// 5. FunctionsNetHost 1 & 2 start, connect to their Sidecar gRPC (placeholder mode)
// 6. User deploys app via SC, triggers specialization
// 7. SC matches Runtime + Worker-1, sends /assign to both (cold start)
// 8. After 10 requests, SC scales out Worker-2 to same Runtime

builder.Build().Run();
