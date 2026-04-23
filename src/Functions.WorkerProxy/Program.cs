// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Azure.Functions.WorkerProxy;
using Microsoft.Azure.Functions.WorkerProxy.Authentication;
using Microsoft.Azure.Functions.WorkerProxy.Configuration;
using Microsoft.Azure.Functions.WorkerProxy.Diagnostics;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Forwarder;

var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = args });
EnsureEnvironmentVariablesConfiguration(builder.Configuration);
var workerProxyEnvironmentOptionsSetup = new WorkerProxyEnvironmentOptionsSetup(builder.Configuration);
var workerProxyEnvironmentOptions = new WorkerProxyEnvironmentOptions();
workerProxyEnvironmentOptionsSetup.Configure(workerProxyEnvironmentOptions);

// Prevent Aspire (or other orchestrators) from overriding our Kestrel configuration
// via ASPNETCORE_URLS. The worker proxy manages its own ports explicitly.
builder.WebHost.UseUrls();

builder.Services.AddOptions<WorkerProxyEnvironmentOptions>();
builder.Services.AddSingleton<IConfigureOptions<WorkerProxyEnvironmentOptions>>(workerProxyEnvironmentOptionsSetup);

if (workerProxyEnvironmentOptions.IsFlexOrLegion)
{
    builder.Logging.ClearProviders();
    builder.Logging.Services.AddSingleton<ILoggerProvider, MsFunctionLogsLoggerProvider>();
}

int runtimeGrpcPort = GetIntArg(args, builder.Configuration, "--runtime-grpc-port", 50051);
int workerGrpcPort = GetIntArg(args, builder.Configuration, "--worker-grpc-port", 50052);
int httpProxyPort = GetIntArg(args, builder.Configuration, "--http-proxy-port", 50053);
int managementPort = GetIntArg(args, builder.Configuration, "--management-port", 50054);
string workerHttpEndpointOverride = GetStringArg(args, builder.Configuration, "--worker-http-endpoint", "http://localhost:8080");
string? hostJsonPath = GetStringArgOrNull(args, builder.Configuration, "--host-json-path");
string httpProxyEndpoint = GetStringArg(args, builder.Configuration, "--http-proxy-endpoint", $"http://localhost:{httpProxyPort}");
string? configuredPodName = GetStringArgOrNull(args, builder.Configuration, "--pod-name");
string podName = !string.IsNullOrWhiteSpace(configuredPodName)
    ? configuredPodName
    : !string.IsNullOrWhiteSpace(workerProxyEnvironmentOptions.ComputerName)
        ? workerProxyEnvironmentOptions.ComputerName
        : System.Net.Dns.GetHostName();

builder.Services.AddSingleton(new RelayOptions(runtimeGrpcPort, workerGrpcPort, httpProxyPort, hostJsonPath, httpProxyEndpoint, podName));
builder.Services.AddSingleton<WorkerPodStateManager>();
builder.Services.AddSingleton<FunctionRpcRelay>();
builder.Services.AddGrpc();
builder.Services.AddHttpForwarder();
builder.Services.AddContainerJwtAuth();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, WorkerProxyJsonContext.Default);
});

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(runtimeGrpcPort, listenOptions => listenOptions.Protocols = HttpProtocols.Http2);
    options.ListenAnyIP(workerGrpcPort, listenOptions => listenOptions.Protocols = HttpProtocols.Http2);
    options.ListenAnyIP(httpProxyPort, listenOptions => listenOptions.Protocols = HttpProtocols.Http1);
    options.ListenAnyIP(managementPort, listenOptions => listenOptions.Protocols = HttpProtocols.Http1);
});

var app = builder.Build();

// HTTP reverse proxy — only handles requests arriving on the proxy port.
var httpClient = new HttpMessageInvoker(new SocketsHttpHandler());
var forwarder = app.Services.GetRequiredService<IHttpForwarder>();

app.Use(async (ctx, next) =>
{
    if (ctx.Connection.LocalPort == httpProxyPort)
    {
        var logger = ctx.RequestServices.GetRequiredService<ILogger<FunctionRpcRelay>>();
        var relayInstance = ctx.RequestServices.GetRequiredService<FunctionRpcRelay>();

        // Resolve destination using the documented precedence:
        //   1. --worker-http-endpoint override (CLI/env), if non-blank.
        //   2. The HttpUri the worker advertised via gRPC WorkerInitResponse (dynamic port).
        //   3. Null → 503.
        // See WorkerHttpDestinationResolver for the full contract and tests.
        string? destination = WorkerHttpDestinationResolver.Resolve(
            workerHttpEndpointOverride,
            relayInstance.WorkerHttpEndpoint);
        if (destination is null)
        {
            logger.LogWarning("[HTTP Proxy] No worker HTTP endpoint available yet "
                + "(no --worker-http-endpoint override and worker has not reported HttpUri). Returning 503.");
            ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        logger.LogDebug("[HTTP Proxy] Received {Method} {Path} on port {Port}, forwarding to {Destination}",
            ctx.Request.Method, ctx.Request.Path, httpProxyPort, destination);
        await forwarder.SendAsync(ctx, destination, httpClient);
        logger.LogDebug("[HTTP Proxy] Forwarded request completed with status {StatusCode}", ctx.Response.StatusCode);
        return;
    }

    await next();
});

app.UseAuthentication();
app.UseAuthorization();

app.MapGrpcService<FunctionRpcRelay>();

// ---------------------------------------------------------------------------
// Management API endpoints (minimal APIs for AOT compatibility).
// Called by NNA on the management port.
//
// /admin/worker/ready is anonymous — NNA polls it before specialization,
// before any encryption key has been delivered, so it cannot present a
// container-issued JWT. All other /admin endpoints require a valid
// container-issued JWT (presented via either the standard Authorization:
// Bearer header or the x-ms-site-token header), matching the runtime's
// /admin/host/assign authentication.
// ---------------------------------------------------------------------------

var stateManager = app.Services.GetRequiredService<WorkerPodStateManager>();
var relay = app.Services.GetRequiredService<FunctionRpcRelay>();

var admin = app.MapGroup("/admin");

admin.MapGet("/worker/ready", () => ManagementApiHandlers.HandleReady(stateManager))
    .AllowAnonymous();

var adminAuthed = admin.MapGroup(string.Empty).RequireAuthorization();

// Worker specialization. NNA calls this after /admin/worker/ready succeeds with the
// app settings payload. The worker proxy drives the full init + specialization +
// metadata prefetch sequence with the worker, caching all responses for later replay
// to the runtime.
adminAuthed.MapPost("/worker/assign", async (HttpContext ctx, CancellationToken cancellationToken) =>
{
    WorkerAssignRequest? assignRequest;
    try
    {
        assignRequest = await ctx.Request.ReadFromJsonAsync(WorkerProxyJsonContext.Default.WorkerAssignRequest, cancellationToken);
    }
    catch (Exception ex)
    {
        return Results.BadRequest($"Invalid request body: {ex.Message}");
    }

    return await ManagementApiHandlers.HandleAssignAsync(assignRequest, stateManager, relay, cancellationToken);
});

adminAuthed.MapPost("/worker/drain", async (HttpContext ctx, CancellationToken cancellationToken) =>
{
    WorkerDrainRequest? drainRequest;
    try
    {
        drainRequest = await ctx.Request.ReadFromJsonAsync(WorkerProxyJsonContext.Default.WorkerDrainRequest, cancellationToken);
    }
    catch (Exception ex)
    {
        return Results.BadRequest($"Invalid request body: {ex.Message}");
    }

    var logger = ctx.RequestServices.GetRequiredService<ILogger<FunctionRpcRelay>>();
    return await ManagementApiHandlers.HandleDrainAsync(drainRequest, stateManager, relay, logger);
});

adminAuthed.MapPost("/infra/instanceState", async (HttpContext ctx, CancellationToken cancellationToken) =>
{
    int clientRevision = 0;

    // Read client's last known revision from request body (if present).
    // Always attempt to read — ContentLength may be null for chunked requests.
    try
    {
        var pollRequest = await ctx.Request.ReadFromJsonAsync(WorkerProxyJsonContext.Default.InstanceStatePollRequest, cancellationToken);
        clientRevision = pollRequest?.Revision ?? 0;
    }
    catch
    {
        // Empty body, malformed JSON, or no content — treat as revision 0 (return current state).
    }

    return await ManagementApiHandlers.HandleInstanceStateAsync(clientRevision, stateManager, cancellationToken);
});

app.Run();

// ---------------------------------------------------------------------------
// Argument helpers — check CLI args first, then environment variables.
// ---------------------------------------------------------------------------

static int GetIntArg(string[] args, IConfiguration configuration, string name, int defaultValue)
{
    int idx = Array.IndexOf(args, name);
    if (idx >= 0 && idx + 1 < args.Length && int.TryParse(args[idx + 1], out int value))
    {
        return value;
    }

    string envKey = ArgNameToEnvKey(name);
    string? envValue = configuration[envKey];
    if (envValue is not null && int.TryParse(envValue, out int envResult))
    {
        return envResult;
    }

    return defaultValue;
}

static string GetStringArg(string[] args, IConfiguration configuration, string name, string defaultValue)
{
    int idx = Array.IndexOf(args, name);
    if (idx >= 0 && idx + 1 < args.Length)
    {
        return args[idx + 1];
    }

    string envKey = ArgNameToEnvKey(name);

    return configuration[envKey] ?? defaultValue;
}

static string? GetStringArgOrNull(string[] args, IConfiguration configuration, string name)
{
    int idx = Array.IndexOf(args, name);
    if (idx >= 0 && idx + 1 < args.Length)
    {
        return args[idx + 1];
    }

    string envKey = ArgNameToEnvKey(name);

    return configuration[envKey];
}

static string ArgNameToEnvKey(string name) =>
    name.TrimStart('-').Replace('-', '_').ToUpperInvariant();

static void EnsureEnvironmentVariablesConfiguration(ConfigurationManager configuration)
{
    ArgumentNullException.ThrowIfNull(configuration);

    if (!configuration.Sources.OfType<EnvironmentVariablesConfigurationSource>().Any())
    {
        configuration.AddEnvironmentVariables();
    }
}
