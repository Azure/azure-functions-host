// Aspire AppHost for the compute separation E2E harness.
// Orchestrates three processes:
//   1. Worker Proxy  – gRPC relay + HTTP proxy
//   2. MockWorker – minimal gRPC worker
//   3. Runtime  – Azure Functions host (WebJobs.Script.WebHost)
//
// Set the "UseContainers" configuration key (or USE_CONTAINERS env var) to "true"
// to run all components as Docker containers instead of local projects.

using System.Text;
using Aspire.Hosting.Azure;
using Azure.Storage.Blobs;

var builder = DistributedApplication.CreateBuilder(args);

bool useContainers = string.Equals(
    builder.Configuration["UseContainers"], "true", StringComparison.OrdinalIgnoreCase);

// --- Ports ---
const int runtimeGrpcPort = 50051;
const int workerGrpcPort = 50052;
const int httpProxyPort = 50053;
const int managementPort = 50054;
const int runtimePort = 7071;
const int mockWorkerHttpPort = 8080;

// --- Well-known values for local development ---
const string HostId = "devhost";
const string MasterKey = "dev-master-key";

// Well-known encryption key for local dev specialization (/admin/instance/assign).
// 64 hex chars = 32 bytes (256-bit AES key). Must match the pre-encrypted payload in compute-separation.http.
const string ContainerEncryptionKey = "0F75CA46E7EBDD39E4CA6B074D1F9A5972B849A55F91A248F6B038A61BACE9D7";

// Well-known Azurite storage emulator account key.
// https://learn.microsoft.com/azure/storage/common/storage-use-azurite#well-known-storage-account-and-key
const string AzuriteAccountKey = "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";

var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator();

if (useContainers)
{
    AddContainerResources(builder, storage);
}
else
{
    AddProjectResources(builder, storage);
}

// --- Seed well-known master key into Azurite ---
// The runtime uses BlobStorageSecretsRepository, which reads from
// azure-webjobs-secrets/{hostId}/host.json. We pre-seed this blob
// so admin APIs can be called with a known key during development.
builder.Eventing.Subscribe<ResourceReadyEvent>(storage.Resource, async (@event, ct) =>
{
    var blobEndpoint = storage.GetEndpoint("blob");

    string scheme = await blobEndpoint.Property(EndpointProperty.Scheme).GetValueAsync(ct) ?? "http";
    string host = await blobEndpoint.Property(EndpointProperty.IPV4Host).GetValueAsync(ct) ?? "127.0.0.1";
    string port = await blobEndpoint.Property(EndpointProperty.Port).GetValueAsync(ct) ?? "10000";
    string blobUrl = $"{scheme}://{host}:{port}/devstoreaccount1;";

    string connectionString = $"DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey={AzuriteAccountKey};BlobEndpoint={blobUrl}";

    var blobServiceClient = new BlobServiceClient(connectionString);
    var containerClient = blobServiceClient.GetBlobContainerClient("azure-webjobs-secrets");
    await containerClient.CreateIfNotExistsAsync(cancellationToken: ct);

    string hostSecretsJson = $$"""
        {
          "masterKey": {
            "name": "master",
            "value": "{{MasterKey}}",
            "encrypted": false
          },
          "functionKeys": [],
          "systemKeys": []
        }
        """;

    await containerClient.UploadBlobAsync($"{HostId}/host.json",
        new BinaryData(Encoding.UTF8.GetBytes(hostSecretsJson)), ct);
});

builder.Build().Run();


/// <summary>
/// Project mode – runs every component as a local .NET project (default).
/// </summary>
// CS-TODO: Project mode currently uses MockWorker for the worker side. Replace with a
// real .NET Isolated worker (SampleIsolatedApp via FunctionsNetHost) to match container
// mode and validate the full end-to-end flow with real function execution locally.
static void AddProjectResources(
    IDistributedApplicationBuilder builder,
    IResourceBuilder<AzureStorageResource> storage)
{
    string runtimeGrpcEndpoint = $"http://localhost:{runtimeGrpcPort}";
    string workerGrpcEndpoint = $"http://localhost:{workerGrpcPort}";
    string runtimeUrl = $"http://localhost:{runtimePort}";

    var runtime = builder.AddProject<Projects.WebJobs_Script_WebHost>("runtime")
        .WithHttpEndpoint(runtimePort, runtimePort, name: "functions-http", isProxied: false)
        .WithEnvironment("FUNCTIONS_WORKER_EXTERNAL_ENABLED", "true")
        // .WithEnvironment("FUNCTIONS_WORKER_EXTERNAL_GRPC_ENDPOINT", runtimeGrpcEndpoint)
        .WithEnvironment("FUNCTIONS_WORKER_RUNTIME", "node")
        .WithEnvironment("AZURE_FUNCTIONS_ENVIRONMENT", "Development")
        .WithEnvironment("AzureFunctionsWebHost__hostid", HostId)
        .WithEnvironment("ASPNETCORE_URLS", runtimeUrl)
        .WithEnvironment("WEBSITE_PLACEHOLDER_MODE", "1")
        .WithEnvironment("WEBSITE_SKU", "FlexConsumption")
        .WithEnvironment("CONTAINER_ENCRYPTION_KEY", ContainerEncryptionKey)
        .WithEnvironment("MESH_INIT_URI", "http://localhost:6060")
        .WithEnvironment(context =>
        {
            ConfigureStorageConnectionString(context, storage);
        })
        .WaitFor(storage);

    var workerProxy = builder.AddProject<Projects.Functions_WorkerProxy>("worker-proxy")
        .WithHttpEndpoint(managementPort, managementPort, name: "management", isProxied: false)
        .WithArgs(
            "--runtime-grpc-port", runtimeGrpcPort.ToString(),
            "--worker-grpc-port", workerGrpcPort.ToString(),
            "--http-proxy-port", httpProxyPort.ToString(),
            "--management-port", managementPort.ToString())
        .WaitFor(runtime);

    var mockWorker = builder.AddProject<Projects.MockWorker>("mock-worker")
        .WithArgs("--grpc-endpoint", workerGrpcEndpoint)
        .WaitFor(workerProxy);
}

/// <summary>
/// Container mode – builds Docker images for each component and runs them.
/// Aspire manages a shared Docker network so containers can address each other
/// by resource name rather than localhost.
/// </summary>
static void AddContainerResources(
    IDistributedApplicationBuilder builder,
    IResourceBuilder<AzureStorageResource> storage)
{
    // Build context is the repository root (relative to the AppHost project directory).
    string repoRoot = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", ".."));

    // --- Worker Proxy (container) ---
    // CS-TODO: The host.json bind mount is a temporary workaround. Once assembly streaming
    // is implemented in a future milestone, the worker will transmit host.json content over
    // gRPC and this bind mount can be removed.
    string hostJsonSource = Path.Combine(repoRoot, "tools", "ComputeSeparation", "SampleIsolatedApp", "host.json");

    var workerProxy = builder.AddDockerfile("worker-proxy", repoRoot, "src/Functions.WorkerProxy/Dockerfile")
        .WithEndpoint(targetPort: runtimeGrpcPort, name: "proxy-runtime-grpc", scheme: "http")
        .WithEndpoint(targetPort: workerGrpcPort, name: "proxy-worker-grpc", scheme: "http")
        .WithEndpoint(targetPort: httpProxyPort, name: "http-proxy", scheme: "http")
        .WithHttpEndpoint(managementPort, managementPort, name: "proxy-management")
        .WithBindMount(hostJsonSource, "/home/site/wwwroot/host.json", isReadOnly: true)
        .WithEnvironment("Logging__LogLevel__Default", "Debug")
        .WithArgs(
            "--runtime-grpc-port", runtimeGrpcPort.ToString(),
            "--worker-grpc-port", workerGrpcPort.ToString(),
            "--http-proxy-port", httpProxyPort.ToString(),
            "--management-port", managementPort.ToString());

    // Tell the proxy its externally-reachable HTTP proxy URL (used in the HttpUri
    // capability rewrite) and the worker's HTTP endpoint for reverse-proxying.
    var proxyHttpEndpoint = workerProxy.GetEndpoint("http-proxy");

    // --- Sample Isolated Worker (container) ---
    // Uses FunctionsNetHost + pre-baked SampleIsolatedApp at /home/site/wwwroot.
    // Fixed container name so the proxy can reach it via Docker DNS on any port,
    // bypassing Aspire's reverse proxy (which only forwards declared endpoints).
    const string workerContainerName = "isolated-worker";

    var isolatedWorker = builder.AddDockerfile("isolated-worker", repoRoot, "tools/ComputeSeparation/SampleIsolatedApp/Dockerfile")
        .WithContainerName(workerContainerName)
        .WithEndpoint(targetPort: mockWorkerHttpPort, name: "worker-http", scheme: "http")
        .WithEnvironment(context =>
        {
            var grpcEndpoint = workerProxy.GetEndpoint("proxy-worker-grpc");
            context.EnvironmentVariables["FUNCTIONS_GRPC_HOST"] = grpcEndpoint.Property(EndpointProperty.Host);
            context.EnvironmentVariables["FUNCTIONS_GRPC_PORT"] = grpcEndpoint.Property(EndpointProperty.Port);
        })
        // Add a second HTTP listener on all interfaces (0.0.0.0) at the known port so the
        // proxy can reach it across Docker container boundaries. The SDK's default listener
        // binds to localhost only, which is unreachable from other containers.
        .WithEnvironment("FUNCTIONS_HTTP_PORT", mockWorkerHttpPort.ToString())
        .WaitFor(workerProxy);

    // Wire proxy env vars that depend on the worker being defined.
    // Use the Docker container name + fixed port directly (not Aspire's *.dev.internal proxy)
    // so the proxy can reach the worker's HTTP listener across container boundaries.
    workerProxy
        .WithEnvironment(context =>
        {
            context.EnvironmentVariables["WORKER_HTTP_ENDPOINT"] = $"http://{workerContainerName}:{mockWorkerHttpPort}";
            context.EnvironmentVariables["HTTP_PROXY_ENDPOINT"] = proxyHttpEndpoint.Property(EndpointProperty.Url);
            var runtimeGrpcEndpont = workerProxy.Resource.GetEndpoint("proxy-runtime-grpc");
            context.EnvironmentVariables["RUNTIME_GRPC_ENDPOINT"] = runtimeGrpcEndpont.Property(EndpointProperty.Url);
        });

    // --- Runtime (container) ---
    var runtime = builder.AddDockerfile("runtime", repoRoot, "src/WebJobs.Script.WebHost/Dockerfile")
        .WithHttpEndpoint(runtimePort, runtimePort, name: "functions-http")
        .WithEnvironment("FUNCTIONS_WORKER_EXTERNAL_ENABLED", "true")
        .WithEnvironment(context =>
        {
            var runtimeGrpc = workerProxy.GetEndpoint("proxy-runtime-grpc");
            // context.EnvironmentVariables["FUNCTIONS_WORKER_EXTERNAL_GRPC_ENDPOINT"] = runtimeGrpc.Property(EndpointProperty.Url);
        })
        .WithEnvironment("FUNCTIONS_WORKER_RUNTIME", "node")
        .WithEnvironment("AZURE_FUNCTIONS_ENVIRONMENT", "Development")
        .WithEnvironment("AzureFunctionsWebHost__hostid", HostId)
        .WithEnvironment("ASPNETCORE_URLS", $"http://+:{runtimePort.ToString()}")
        .WithEnvironment("WEBSITE_PLACEHOLDER_MODE", "1")
        .WithEnvironment("WEBSITE_SKU", "FlexConsumption")
        .WithEnvironment("CONTAINER_ENCRYPTION_KEY", ContainerEncryptionKey)
        .WithEnvironment("MESH_INIT_URI", "http://localhost:6060")
        .WithEnvironment("CONSOLE_LOGGING_DISABLED", "1")
        .WithEnvironment(context =>
        {
            ConfigureStorageConnectionString(context, storage);
        })
        .WaitFor(storage);

    isolatedWorker.WaitFor(workerProxy);
}

static void ConfigureStorageConnectionString(
    EnvironmentCallbackContext context,
    IResourceBuilder<AzureStorageResource> storage)
{
    var blob = storage.GetEndpoint("blob");
    var queue = storage.GetEndpoint("queue");
    var table = storage.GetEndpoint("table");

    // Use IPV4Host (127.0.0.1) instead of Host/Url (localhost) to avoid IPv6 resolution
    // issues with Azurite. This matches how Aspire's AzureStorageEmulatorConnectionString
    // builds the connection string internally.
    context.EnvironmentVariables["AzureWebJobsStorage"] =
        ReferenceExpression.Create(
            $"DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey={AzuriteAccountKey};{EmulatorEndpoint("BlobEndpoint", blob)}{EmulatorEndpoint("QueueEndpoint", queue)}{EmulatorEndpoint("TableEndpoint", table)}");

    static ReferenceExpression EmulatorEndpoint(string key, EndpointReference endpoint)
        => ReferenceExpression.Create(
            $"{key}={endpoint.Property(EndpointProperty.Scheme)}://{endpoint.Property(EndpointProperty.IPV4Host)}:{endpoint.Property(EndpointProperty.Port)}/devstoreaccount1;");
}