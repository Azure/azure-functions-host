// Aspire AppHost for the compute separation E2E harness.
// Orchestrates three processes:
//   1. Worker Proxy  – gRPC relay + HTTP proxy
//   2. MockWorker – minimal gRPC worker
//   3. Runtime  – Azure Functions host (WebJobs.Script.WebHost)

var builder = DistributedApplication.CreateBuilder(args);

// --- Ports ---
const int runtimeGrpcPort = 50051;
const int workerGrpcPort = 50052;
const int httpProxyPort = 50053;
const int runtimePort = 7071;

// --- Formatted endpoint strings ---
string runtimeGrpcEndpoint = $"http://localhost:{runtimeGrpcPort}";
string workerGrpcEndpoint = $"http://localhost:{workerGrpcPort}";
string runtimeUrl = $"http://localhost:{runtimePort}";

// Well-known Azurite storage emulator account key.
// https://learn.microsoft.com/azure/storage/common/storage-use-azurite#well-known-storage-account-and-key
const string AzuriteAccountKey = "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";

var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator();


// --- Runtime ---
// Note: Aspire's AddAzureFunctionsProject handles AzureWebJobsStorage injection
// automatically via WithHostStorage. Since we use AddProject (the runtime isn't a
// standard Functions project from Aspire's perspective), we wire it manually.
// The emulator connection string is injected after the storage emulator starts.
var runtime = builder.AddProject<Projects.WebJobs_Script_WebHost>("runtime")
    .WithHttpEndpoint(runtimePort, runtimePort, name: "functions-http", isProxied: false)
    .WithEnvironment("FUNCTIONS_WORKER_EXTERNAL_ENABLED", "true")
    .WithEnvironment("FUNCTIONS_WORKER_EXTERNAL_GRPC_ENDPOINT", runtimeGrpcEndpoint)
    .WithEnvironment("FUNCTIONS_WORKER_RUNTIME", "node")
    .WithEnvironment("AZURE_FUNCTIONS_ENVIRONMENT", "Development")
    .WithEnvironment("ASPNETCORE_URLS", runtimeUrl)
    .WithEnvironment(context =>
    {
        var blob = storage.GetEndpoint("blob");
        var queue = storage.GetEndpoint("queue");
        var table = storage.GetEndpoint("table");
        context.EnvironmentVariables["AzureWebJobsStorage"] =
            ReferenceExpression.Create(
                $"DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey={AzuriteAccountKey};BlobEndpoint={blob.Property(EndpointProperty.Url)}/devstoreaccount1;QueueEndpoint={queue.Property(EndpointProperty.Url)}/devstoreaccount1;TableEndpoint={table.Property(EndpointProperty.Url)}/devstoreaccount1;");
    })
    .WaitFor(storage);

// --- Worker Proxy ---
var workerProxy = builder.AddProject<Projects.Functions_WorkerProxy>("worker-proxy")
    .WithArgs(
        "--runtime-grpc-port", runtimeGrpcPort.ToString(),
        "--worker-grpc-port", workerGrpcPort.ToString(),
        "--http-proxy-port", httpProxyPort.ToString())
    .WaitFor(runtime);

// --- Mock Worker ---
var mockWorker = builder.AddProject<Projects.MockWorker>("mock-worker")
    .WithArgs("--grpc-endpoint", workerGrpcEndpoint)
    .WaitFor(workerProxy);

builder.Build().Run();
