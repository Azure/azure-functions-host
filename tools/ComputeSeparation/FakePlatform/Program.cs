// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

// Local stand-in for the platform in the compute harness. It sends the assign and link requests to the WorkerProxy and
// Host, and receives the Host's HTTP capacity as AppServer would.

using System.Buffers.Text;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
FakePlatformOptions options = builder.Configuration.GetSection("FakePlatform").Get<FakePlatformOptions>()
    ?? throw new InvalidOperationException("The FakePlatform configuration section is required.");
ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxConcurrency);
builder.Services.AddHealthChecks();
builder.Services.AddHttpClient(HostClient, client => client.BaseAddress = options.HostUri);
builder.Services.AddHttpClient(WorkerProxyClient, client => client.BaseAddress = options.WorkerProxyUri);

WebApplication app = builder.Build();
ILogger logger = app.Logger;
Lock hostStateLock = new();
HostState? acceptedHostState = null;

// Called by the AppHost health check.
app.MapHealthChecks("/health");

// Called by the Host (FUNCTIONS_APPSERVER_URI) whenever its HTTP capacity changes. Keeps only the
// newest snapshot and ignores one whose version is not newer than the last accepted.
app.MapPut("/admin/infra/host/state", (HostState state) =>
{
    lock (hostStateLock)
    {
        bool stale = acceptedHostState is not null && state.SnapshotVersion <= acceptedHostState.SnapshotVersion;
        if (!stale)
        {
            acceptedHostState = state;
        }

        logger.LogInformation("Host state {SnapshotVersion}: HTTP capacity {HttpCapacity}{Stale}",
            state.SnapshotVersion, state.HttpCapacity, stale ? " (stale, ignored)" : string.Empty);
    }

    return Results.Ok();
});

// Simulation routes: harness-only, never called by the Host or WorkerProxy.
RouteGroupBuilder simulate = app.MapGroup("/simulate");

// Called by AppHost or a developer to assign the worker pod on the WorkerProxy (PUT /admin/worker/assignment).
simulate.MapPost("/worker/assign", async (IHttpClientFactory clients, CancellationToken cancellationToken) =>
{
    WorkerAssignment assignment = new("Preconfigured", AppName, "http", false, [], string.Empty, options.MaxConcurrency);
    using HttpResponseMessage response = await clients.CreateClient(WorkerProxyClient)
        .PutAsJsonAsync("/admin/worker/assignment", assignment, cancellationToken);

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
    string context = JsonSerializer.Serialize(new HostAssignmentContext(1, AppName, []), JsonSerializerOptions.Web);
    using HttpRequestMessage request = new(HttpMethod.Post, "/admin/instance/assign")
    {
        Content = JsonContent.Create(new HostAssignmentRequest(Encrypt(key, context))),
    };
    request.Headers.Authorization = new("Bearer", CreateAdminToken(key));

    using HttpResponseMessage response = await clients.CreateClient(HostClient).SendAsync(request, cancellationToken);
    return await ToResultAsync("host assign", response, cancellationToken);
});

// Called by AppHost or a developer, after assignment, to link the worker to the Host (PUT /admin/workers/{workerId}).
simulate.MapPost("/worker/link", async (IHttpClientFactory clients, CancellationToken cancellationToken) =>
{
    WorkerLink link = new(options.WorkerGrpcEndpoint.ToString());
    using HttpResponseMessage response = await clients.CreateClient(HostClient)
        .PutAsJsonAsync($"/admin/workers/{Uri.EscapeDataString(options.WorkerId)}", link, cancellationToken);

    return await ToResultAsync("worker link", response, cancellationToken);
});

// Called by a developer to read the last Host state accepted by PUT /admin/infra/host/state.
simulate.MapGet("/host/state", () =>
{
    lock (hostStateLock)
    {
        return acceptedHostState is null ? Results.NoContent() : Results.Ok(acceptedHostState);
    }
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
    private const string WorkerProxyClient = "worker-proxy";

    private sealed class FakePlatformOptions
    {
        public required Uri HostUri { get; init; }

        public required Uri WorkerProxyUri { get; init; }

        public required Uri WorkerGrpcEndpoint { get; init; }

        public required string WorkerId { get; init; }

        public int MaxConcurrency { get; init; } = 16;

        // Hex key shared with the Host (CONTAINER_ENCRYPTION_KEY); only set in `project-placeholder-manual`.
        public string? EncryptionKey { get; init; }
    }

    private sealed record HostAssignmentContext(int SiteId, string SiteName, Dictionary<string, string> Environment);

    private sealed record HostAssignmentRequest(string EncryptedContext);

    private sealed record WorkerAssignment(string StartupMode, string FunctionAppName, string FunctionGroupName,
        bool IsAlwaysReady, Dictionary<string, string> Environment, string FunctionAppDirectory, int MaxConcurrency);

    private sealed record WorkerLink(string WorkerGrpcEndpoint);

    private sealed record HostState(long SnapshotVersion, long HttpCapacity);
}
