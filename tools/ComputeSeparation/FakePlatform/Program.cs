// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

// Local stand-in for the platform in the compute harness. It sends the assign and link requests to the WorkerProxy and
// Host.

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.Functions.ComputeSeparation;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
FakePlatformOptions options = builder.Configuration.GetSection("FakePlatform").Get<FakePlatformOptions>()
    ?? throw new InvalidOperationException("The FakePlatform configuration section is required.");
builder.Services.AddHealthChecks();
builder.Services.AddHttpClient(HostClient, client =>
{
    client.BaseAddress = options.HostUri;
    client.Timeout = Timeout.InfiniteTimeSpan;
});
foreach (WorkerProxyOptions workerProxy in options.WorkerProxies)
{
    builder.Services.AddHttpClient(workerProxy.Name, client => client.BaseAddress = workerProxy.ManagementUri);
}

WebApplication app = builder.Build();
ILogger logger = app.Logger;

// Called by the AppHost health check.
app.MapHealthChecks("/health");

// Simulation routes: harness-only, never called by the Host or WorkerProxy.
RouteGroupBuilder simulate = app.MapGroup("/simulate");

// Called by AppHost or a developer to assign the worker pod on the WorkerProxy (PUT /admin/worker/assignment).
simulate.MapPost("/worker/{workerProxyName}/assign", async (string workerProxyName, IHttpClientFactory clients, CancellationToken cancellationToken) =>
{
    WorkerProxyOptions? workerProxy = Array.Find(options.WorkerProxies,
        proxy => string.Equals(proxy.Name, workerProxyName, StringComparison.Ordinal));
    if (workerProxy is null)
    {
        Log.UnknownWorkerProxy(logger, workerProxyName);
        return Results.NotFound($"Unknown worker proxy '{workerProxyName}'.");
    }

    WorkerAssignment assignment = new("Preconfigured", AppName, "http", false, [], string.Empty,
        workerProxy.WorkerHttpEndpoint.GetComponents(UriComponents.HostAndPort, UriFormat.UriEscaped), 0, 0, 0);
    using HttpRequestMessage request = new(HttpMethod.Put, "/admin/worker/assignment") { Content = JsonContent.Create(assignment) };
    request.Headers.Add("x-ms-site-token", ManagementToken.Create(workerProxy.RuntimeAccessKey, workerProxy.WorkerId));
    using HttpResponseMessage response = await clients.CreateClient(workerProxy.Name).SendAsync(request, cancellationToken);

    return await ToResultAsync("worker assign", response, cancellationToken);
});

// Called by a developer in the `project-placeholder-manual` profile, before the link, to assign the placeholder Host to
// the sample app (POST /admin/instance/assign). The Host accepts with 202, then leaves placeholder mode and specializes.
simulate.MapPost("/host/assign", async (IHttpClientFactory clients, CancellationToken cancellationToken) =>
{
    if (options.EncryptionKey is null)
    {
        return Results.Conflict("Host assignment requires the project-placeholder-manual profile.");
    }

    byte[] key = Convert.FromHexString(options.EncryptionKey);
    string context = JsonSerializer.Serialize(new HostAssignmentContext(1, AppName,
        new() { ["WEBSITE_AUTH_ENCRYPTION_KEY"] = options.AppAuthKey }), JsonSerializerOptions.Web);
    using HttpRequestMessage request = new(HttpMethod.Post, "/admin/instance/assign")
    {
        Content = JsonContent.Create(new HostAssignmentRequest(Encrypt(key, context))),
    };
    request.Headers.Authorization = new("Bearer", CreateAdminToken(key));

    using HttpResponseMessage response = await clients.CreateClient(HostClient).SendAsync(request, cancellationToken);
    return await ToResultAsync("host assign", response, cancellationToken);
});

// Called by AppHost or a developer, after assignment, to link the worker to the Host (PUT /admin/workers/{workerId}).
simulate.MapPost("/worker/{workerProxyName}/link", async (string workerProxyName, IHttpClientFactory clients, CancellationToken cancellationToken) =>
{
    WorkerProxyOptions? workerProxy = Array.Find(options.WorkerProxies,
        proxy => string.Equals(proxy.Name, workerProxyName, StringComparison.Ordinal));
    if (workerProxy is null)
    {
        Log.UnknownWorkerProxy(logger, workerProxyName);
        return Results.NotFound($"Unknown worker proxy '{workerProxyName}'.");
    }

    var link = new
    {
        workerPodName = workerProxy.WorkerId,
        workerGrpcEndpoint = workerProxy.WorkerGrpcEndpoint.GetComponents(UriComponents.HostAndPort, UriFormat.UriEscaped),
        runtimeAccessKey = workerProxy.RuntimeAccessKey
    };
    using HttpRequestMessage request = new(HttpMethod.Put, $"/admin/workers/{Uri.EscapeDataString(workerProxy.WorkerId)}")
    {
        Content = JsonContent.Create(link)
    };
    request.Headers.Add("x-ms-site-token", ManagementToken.Create(options.RuntimeContainerKey, options.RuntimePodName));
    using HttpResponseMessage response = await clients.CreateClient(HostClient).SendAsync(request, cancellationToken);

    return await ToResultAsync("worker link", response, cancellationToken);
});

app.Run();

// Relays the downstream response so the caller sees exactly what the WorkerProxy or Host returned.
async Task<IResult> ToResultAsync(string operation, HttpResponseMessage response, CancellationToken cancellationToken)
{
    string body = await response.Content.ReadAsStringAsync(cancellationToken);
    logger.LogInformation("{Operation} request to {Uri} -> {Status}", operation, response.RequestMessage?.RequestUri,
        (int)response.StatusCode);

    return Results.Content(body, response.Content.Headers.ContentType?.ToString(), statusCode: (int)response.StatusCode);
}

// Encrypts the assignment context in the format the Host decrypts: {iv}.{ciphertext}, both base64 (AES-CBC).
static string Encrypt(byte[] key, string value)
{
    using Aes aes = Aes.Create();
    aes.Key = key;
    byte[] cipherText = aes.EncryptCbc(Encoding.UTF8.GetBytes(value), aes.IV);

    return $"{Convert.ToBase64String(aes.IV)}.{Convert.ToBase64String(cipherText)}";
}

// Creates a short-lived token the Host accepts for admin requests: a JWT signed (HS256) with the shared key.
static string CreateAdminToken(byte[] key)
{
    string header = Base64Url.EncodeToString("""{"alg":"HS256","typ":"JWT"}"""u8);
    string payload = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(new
    {
        iss = "https://appservice.core.azurewebsites.net",
        aud = $"https://{AppName}.azurewebsites.net/azurefunctions",
        exp = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds(),
    }));
    string signature = Base64Url.EncodeToString(HMACSHA256.HashData(key, Encoding.ASCII.GetBytes($"{header}.{payload}")));

    return $"{header}.{payload}.{signature}";
}

internal partial class Program
{
    // The app name used for assignments, and the Host's site name (WEBSITE_SITE_NAME) in the
    // `project-placeholder-manual` profile.
    private const string AppName = "aspire-sample-app";
    private const string HostClient = "host";

    private sealed class FakePlatformOptions
    {
        public required Uri HostUri { get; init; }

        public required WorkerProxyOptions[] WorkerProxies { get; init; }

        public required string AppAuthKey { get; init; }

        public required string RuntimeContainerKey { get; init; }

        public required string RuntimePodName { get; init; }

        // Hex key shared with the Host (CONTAINER_ENCRYPTION_KEY); only set in placeholder profiles.
        public string? EncryptionKey { get; init; }
    }

    private sealed class WorkerProxyOptions
    {
        public required string Name { get; init; }

        public required Uri ManagementUri { get; init; }

        public required Uri WorkerGrpcEndpoint { get; init; }

        public required Uri WorkerHttpEndpoint { get; init; }

        public required string RuntimeAccessKey { get; init; }

        public required string WorkerId { get; init; }
    }

    private sealed record HostAssignmentContext(int SiteId, string SiteName, Dictionary<string, string> Environment);

    private sealed record HostAssignmentRequest(string EncryptedContext);

    private sealed record WorkerAssignment(string StartupMode, string FunctionAppName, string FunctionGroupName,
        bool IsAlwaysReady, Dictionary<string, string> Environment, string FunctionAppDirectory, string HttpProxyEndpoint,
        long ConfigLastModifiedTime, long ContentLastModifiedTime, int MaxHttpConcurrency);

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Warning, "No worker proxy named {WorkerProxyName} is configured.")]
        public static partial void UnknownWorkerProxy(ILogger logger, string workerProxyName);
    }
}
