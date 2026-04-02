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
string workerHttpEndpoint = GetStringArg(args, "--worker-http-endpoint", "http://localhost:8080");
string? hostJsonPath = GetStringArgOrNull(args, "--host-json-path");
string httpProxyEndpoint = GetStringArg(args, "--http-proxy-endpoint", $"http://localhost:{httpProxyPort}");

builder.Services.AddSingleton(new RelayOptions(runtimeGrpcPort, workerGrpcPort, httpProxyPort, hostJsonPath, httpProxyEndpoint));
builder.Services.AddSingleton<FunctionRpcRelay>();
builder.Services.AddGrpc();
builder.Services.AddHttpForwarder();

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(runtimeGrpcPort, listenOptions => listenOptions.Protocols = HttpProtocols.Http2);
    options.ListenAnyIP(workerGrpcPort, listenOptions => listenOptions.Protocols = HttpProtocols.Http2);
    options.ListenAnyIP(httpProxyPort, listenOptions => listenOptions.Protocols = HttpProtocols.Http1);
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
