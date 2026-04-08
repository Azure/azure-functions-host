// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.Functions.WorkerProxy;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Yarp.ReverseProxy.Forwarder;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args });

// Prevent Aspire (or other orchestrators) from overriding our Kestrel configuration
// via ASPNETCORE_URLS. The worker proxy manages its own ports explicitly.
builder.WebHost.UseUrls();

int runtimeGrpcPort = GetIntArg(args, "--runtime-grpc-port", 50051);
int workerGrpcPort = GetIntArg(args, "--worker-grpc-port", 50052);
int httpProxyPort = GetIntArg(args, "--http-proxy-port", 50053);
int managementPort = GetIntArg(args, "--management-port", 50054);
string workerHttpEndpoint = GetStringArg(args, "--worker-http-endpoint", "http://localhost:8080");
string? hostJsonPath = GetStringArgOrNull(args, "--host-json-path");
string httpProxyEndpoint = GetStringArg(args, "--http-proxy-endpoint", $"http://localhost:{httpProxyPort}");

builder.Services.AddSingleton(new RelayOptions(runtimeGrpcPort, workerGrpcPort, httpProxyPort, hostJsonPath, httpProxyEndpoint));
builder.Services.AddSingleton<WorkerPodStateManager>();
builder.Services.AddSingleton<FunctionRpcRelay>();
builder.Services.AddGrpc();
builder.Services.AddHttpForwarder();

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
        await forwarder.SendAsync(ctx, workerHttpEndpoint, httpClient);
        return;
    }

    await next();
});

app.MapGrpcService<FunctionRpcRelay>();

// ---------------------------------------------------------------------------
// Management API endpoints (minimal APIs for AOT compatibility).
// Called by NNA on the management port.
// ---------------------------------------------------------------------------

var stateManager = app.Services.GetRequiredService<WorkerPodStateManager>();
var relay = app.Services.GetRequiredService<FunctionRpcRelay>();

app.MapGet("/ready", () =>
{
    return stateManager.CurrentStatus >= WorkerPodStatus.ReadyForRequest
        ? Results.Ok()
        : Results.StatusCode(503);
});

// [CS-TODO] Implement worker specialization. NNA calls this after /ready succeeds
// with the app settings payload. The worker proxy should forward this to the language
// worker as a FunctionEnvironmentReloadRequest over gRPC, causing the worker to
// apply environment variables and load customer code.
app.MapPost("/assign", () =>
{
    return Results.Ok();
});

app.MapPost("/drain", async () =>
{
    stateManager.UpdatePodStatus(WorkerPodStatus.Draining);
    await relay.SendDrainRequestToRuntimeAsync();
    return Results.Accepted();
});

app.MapPost("/instanceState", async (HttpContext ctx, CancellationToken cancellationToken) =>
{
    int clientRevisionId = 0;

    // Read client's last known revision from request body (if present).
    if (ctx.Request.ContentLength > 0)
    {
        try
        {
            var clientState = await ctx.Request.ReadFromJsonAsync<WorkerPodState>(cancellationToken);
            clientRevisionId = clientState?.RevisionId ?? 0;
        }
        catch
        {
            // If body can't be parsed, treat as revision 0 (return current state).
        }
    }

    var result = await stateManager.WaitForChangeAsync(clientRevisionId, cancellationToken);

    if (result is null)
    {
        return Results.NoContent();
    }

    return Results.Ok(result);
});

app.Run();

// ---------------------------------------------------------------------------
// Argument helpers — check CLI args first, then environment variables.
// ---------------------------------------------------------------------------

static int GetIntArg(string[] args, string name, int defaultValue)
{
    int idx = Array.IndexOf(args, name);
    if (idx >= 0 && idx + 1 < args.Length && int.TryParse(args[idx + 1], out int value))
    {
        return value;
    }

    string envKey = ArgNameToEnvKey(name);
    string? envValue = Environment.GetEnvironmentVariable(envKey);
    if (envValue is not null && int.TryParse(envValue, out int envResult))
    {
        return envResult;
    }

    return defaultValue;
}

static string GetStringArg(string[] args, string name, string defaultValue)
{
    int idx = Array.IndexOf(args, name);
    if (idx >= 0 && idx + 1 < args.Length)
    {
        return args[idx + 1];
    }

    string envKey = ArgNameToEnvKey(name);

    return Environment.GetEnvironmentVariable(envKey) ?? defaultValue;
}

static string? GetStringArgOrNull(string[] args, string name)
{
    int idx = Array.IndexOf(args, name);
    if (idx >= 0 && idx + 1 < args.Length)
    {
        return args[idx + 1];
    }

    string envKey = ArgNameToEnvKey(name);

    return Environment.GetEnvironmentVariable(envKey);
}

static string ArgNameToEnvKey(string name) =>
    name.TrimStart('-').Replace('-', '_').ToUpperInvariant();
