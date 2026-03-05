using Microsoft.AspNetCore.Server.Kestrel.Core;
using WorkerModel.Sidecar.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Configure Kestrel endpoints:
// - REST endpoints: From ASPNETCORE_URLS (managed by Aspire, proxied)
// - gRPC endpoint: Dedicated HTTP/2-only port for FunctionsNetHost
//
// CRITICAL: gRPC over cleartext requires HttpProtocols.Http2 (h2c).
// Http1AndHttp2 does NOT support h2c without TLS.
var grpcPortStr = Environment.GetEnvironmentVariable("SIDECAR_GRPC_PORT");
if (!string.IsNullOrEmpty(grpcPortStr) && int.TryParse(grpcPortStr, out var grpcPort))
{
    builder.WebHost.ConfigureKestrel(options =>
    {
        // Parse Aspire's assigned URLs and re-add them explicitly
        var urls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://localhost:5085";
        foreach (var urlStr in urls.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var url = urlStr.Trim();
            if (string.IsNullOrEmpty(url)) continue;
            
            var uri = new Uri(url);
            // Skip if this is the gRPC port (we'll add it separately with HTTP/2)
            if (uri.Port == grpcPort) continue;
            
            options.Listen(System.Net.IPAddress.Any, uri.Port, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http1;
            });
            Console.WriteLine($"[Sidecar] HTTP endpoint: http://0.0.0.0:{uri.Port} (REST/health)");
        }

        // gRPC endpoint: HTTP/2 only (required for h2c/cleartext gRPC)
        options.Listen(System.Net.IPAddress.Any, grpcPort, listenOptions =>
        {
            listenOptions.Protocols = HttpProtocols.Http2;
        });
        Console.WriteLine($"[Sidecar] gRPC endpoint: http://0.0.0.0:{grpcPort} (HTTP/2 only)");
    });
}

// Add gRPC services
builder.Services.AddGrpc();

// Add HTTP client factory for calling Scale Controller
builder.Services.AddHttpClient();

// Add singleton services for state management
builder.Services.AddSingleton<WorkerState>();
builder.Services.AddSingleton<RuntimeConnectionManager>();
builder.Services.AddSingleton<SpecializationService>();

// Add Scale Controller registration as hosted service
builder.Services.AddHostedService<ScaleControllerRegistration>();

// Add controllers for HTTP endpoints
builder.Services.AddControllers();

var app = builder.Build();

// Map HTTP endpoints
app.MapControllers();

// Map gRPC service (for FunctionsNetHost to connect to)
// Uses the actual FunctionRpc service interface
app.MapGrpcService<SidecarRpcService>();

// Note: /health routes are handled by HealthController (not a minimal API)
// HealthController provides: GET /health (status), GET /health/live (liveness), GET /health/ready (readiness)

Console.WriteLine("[Sidecar] Starting Worker Model Sidecar...");
Console.WriteLine($"[Sidecar] Worker ID: {Environment.GetEnvironmentVariable("SIDECAR_WORKER_ID") ?? "auto-generated"}");
Console.WriteLine($"[Sidecar] Scale Controller: {Environment.GetEnvironmentVariable("services__scalecontroller__http__0") ?? Environment.GetEnvironmentVariable("SCALE_CONTROLLER_ENDPOINT") ?? "not configured"}");

app.Run();
