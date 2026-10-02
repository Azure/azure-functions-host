// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

// Local stand-in for the platform in the compute harness. It sends the assign and link requests to the WorkerProxy and
// Host.

using System.Buffers.Text;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
FakePlatformOptions options = builder.Configuration.GetSection("FakePlatform").Get<FakePlatformOptions>()
    ?? throw new InvalidOperationException("The FakePlatform configuration section is required.");
builder.Services.AddHealthChecks();
builder.Services.AddHttpClient(HostClient, client => client.BaseAddress = options.HostUri);
builder.Services.AddHttpClient(WorkerProxyClient, client => client.BaseAddress = options.WorkerProxyUri);
if (options.SecondWorker is { } secondWorker)
{
    if (string.Equals(options.WorkerId, secondWorker.WorkerId, StringComparison.Ordinal) ||
        options.WorkerProxyUri == secondWorker.WorkerProxyUri || options.WorkerGrpcEndpoint == secondWorker.WorkerGrpcEndpoint)
    {
        throw new InvalidOperationException("The second worker must have a distinct ID and distinct proxy endpoints.");
    }

    builder.Services.AddHttpClient(SecondWorkerProxyClient, client => client.BaseAddress = secondWorker.WorkerProxyUri);
}

WebApplication app = builder.Build();
ILogger logger = app.Logger;

// Called by the AppHost health check.
app.MapHealthChecks("/health");

// Simulation routes: harness-only, never called by the Host or WorkerProxy.
RouteGroupBuilder simulate = app.MapGroup("/simulate");

// Called by AppHost or a developer to assign the worker pod on the WorkerProxy (PUT /admin/worker/assignment).
simulate.MapPost("/worker/assign", async (string? worker, IHttpClientFactory clients, CancellationToken cancellationToken) =>
{
    if (!TrySelectWorker(worker, out WorkerOptions selected, out string proxyClient))
    {
        return Results.BadRequest(InvalidWorkerMessage);
    }

    WorkerAssignment assignment = new("Preconfigured", AppName, "http", false, [], string.Empty);
    using HttpResponseMessage response = await clients.CreateClient(proxyClient)
        .PutAsJsonAsync("/admin/worker/assignment", assignment, cancellationToken);

    return await ToResultAsync($"worker assign ({selected.WorkerId})", response, cancellationToken);
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
simulate.MapPost("/worker/link", async (string? worker, IHttpClientFactory clients, CancellationToken cancellationToken) =>
{
    if (!TrySelectWorker(worker, out WorkerOptions selected, out _))
    {
        return Results.BadRequest(InvalidWorkerMessage);
    }

    WorkerLink link = new(selected.WorkerGrpcEndpoint.ToString());
    using HttpResponseMessage response = await clients.CreateClient(HostClient)
        .PutAsJsonAsync($"/admin/workers/{Uri.EscapeDataString(selected.WorkerId)}", link, cancellationToken);

    return await ToResultAsync($"worker link ({selected.WorkerId})", response, cancellationToken);
});

app.Run();

bool TrySelectWorker(string? worker, out WorkerOptions selected, out string proxyClient)
{
    selected = options;
    proxyClient = WorkerProxyClient;
    if (worker is null || string.Equals(worker, "first", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    if (string.Equals(worker, "second", StringComparison.OrdinalIgnoreCase) && options.SecondWorker is { } second)
    {
        selected = second;
        proxyClient = SecondWorkerProxyClient;
        return true;
    }

    return false;
}

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
    private const string SecondWorkerProxyClient = "worker-proxy-2";
    private const string InvalidWorkerMessage = "Select worker=first (the default), or worker=second with ComputeSeparation:EnableSecondWorker enabled.";

    private sealed class FakePlatformOptions : WorkerOptions
    {
        public required Uri HostUri { get; init; }

        public WorkerOptions? SecondWorker { get; init; }

        // Hex key shared with the Host (CONTAINER_ENCRYPTION_KEY); only set in `project-placeholder-manual`.
        public string? EncryptionKey { get; init; }
    }

    private class WorkerOptions
    {
        public required Uri WorkerProxyUri { get; init; }

        public required Uri WorkerGrpcEndpoint { get; init; }

        public required string WorkerId { get; init; }
    }

    private sealed record HostAssignmentContext(int SiteId, string SiteName, Dictionary<string, string> Environment);

    private sealed record HostAssignmentRequest(string EncryptedContext);

    private sealed record WorkerAssignment(string StartupMode, string FunctionAppName, string FunctionGroupName,
        bool IsAlwaysReady, Dictionary<string, string> Environment, string FunctionAppDirectory);

    private sealed record WorkerLink(string WorkerGrpcEndpoint);
}
