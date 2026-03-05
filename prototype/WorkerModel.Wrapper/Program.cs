using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorkerModel.Wrapper;

// Wrapper - PID 1 process supervisor for FunctionsNetHost
// Responsibilities:
// - Start and monitor FunctionsNetHost process
// - Forward signals (SIGTERM/SIGINT) to worker
// - Expose restart API via Unix socket (for failure recovery)
// - Exit if worker exits (let k8s restart pod)

var builder = Host.CreateApplicationBuilder(args);

// Add Aspire service defaults (service discovery, resilience, telemetry)
builder.AddServiceDefaults();

// Register WrapperConfig from environment
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetService<IConfiguration>();

    // Check for Aspire service discovery endpoint first
    // Aspire injects: services__<sidecar-name>__grpc__0=http://host:port
    // SIDECAR_SERVICE_NAME tells us which sidecar to look up (defaults to worker-sidecar1)
    var sidecarServiceName = Environment.GetEnvironmentVariable("SIDECAR_SERVICE_NAME") ?? "worker-sidecar1";
    var sidecarGrpcEndpoint = Environment.GetEnvironmentVariable($"services__{sidecarServiceName}__grpc__0");
    if (!string.IsNullOrEmpty(sidecarGrpcEndpoint))
    {
        // Override FUNCTIONS_URI with discovered endpoint
        Environment.SetEnvironmentVariable("FUNCTIONS_URI", sidecarGrpcEndpoint);
    }

    return WrapperConfig.FromEnvironment();
});

// Register the process manager
builder.Services.AddSingleton<WorkerProcessManager>();

// Register the worker lifecycle service
builder.Services.AddHostedService<WorkerLifecycleService>();

var host = builder.Build();

// Log startup info
var logger = host.Services.GetRequiredService<ILogger<Program>>();
var config = host.Services.GetRequiredService<WrapperConfig>();
logger.LogInformation("[Wrapper] Starting with config:");
logger.LogInformation("  Worker ID: {WorkerId}", config.WorkerId);
logger.LogInformation("  Functions URI: {FunctionsUri}", config.FunctionsUri);
logger.LogInformation("  NetHost Path: {NetHostPath}", config.FunctionsNetHostPath);
logger.LogInformation("  Script Root: {ScriptRoot}", config.ScriptRoot);

await host.RunAsync();

