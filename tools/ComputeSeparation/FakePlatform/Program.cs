// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

// Local stand-in for the platform in the compute harness. It sends the assign and link requests to the WorkerProxy
// and Host, and records the linked worker counts the Host publishes to its configured state endpoint.

using System.Net.Http.Json;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
FakePlatformOptions options = builder.Configuration.GetSection("FakePlatform").Get<FakePlatformOptions>()
    ?? throw new InvalidOperationException("The FakePlatform configuration section is required.");
builder.Services.AddHttpClient(HostClient, client => client.BaseAddress = options.HostUri);
builder.Services.AddHttpClient(WorkerProxyClient, client => client.BaseAddress = options.WorkerProxyUri);

WebApplication app = builder.Build();
ILogger logger = app.Logger;

Lock gate = new();
List<ReceivedPush> history = [];
HostState? accepted = null;

// Real contract: the only route the Host calls. It mirrors the platform's host-state endpoint.
// Called by the Functions Host whenever its linked worker counts change, including at startup and shutdown.
app.MapPut("/admin/infra/host/state", (HostState state) =>
{
    lock (gate)
    {
        bool stale = accepted is not null && state.SnapshotVersion <= accepted.SnapshotVersion;
        if (!stale)
        {
            accepted = state;
        }

        history.Add(new(DateTimeOffset.UtcNow, state, stale));
        logger.LogInformation(
            "Host state push: version={Version}, linkedWorkerCount={Linked}, linkedHttpWorkerCount={Http}, stale={Stale}",
            state.SnapshotVersion, state.LinkedWorkerCount, state.LinkedHttpWorkerCount, stale);

        return Results.Ok();
    }
});

// Simulation and inspection routes: harness-only, never called by the Host or WorkerProxy.
RouteGroupBuilder simulate = app.MapGroup("/simulate");

// Called by AppHost or a developer to assign the worker pod on the WorkerProxy (PUT /admin/worker/assignment).
simulate.MapPost("/worker/assign", async (IHttpClientFactory clients, CancellationToken cancellationToken) =>
{
    WorkerAssignment assignment = new("Preconfigured", "aspire-sample-app", "http", false, [], string.Empty);
    using HttpResponseMessage response = await clients.CreateClient(WorkerProxyClient)
        .PutAsJsonAsync("/admin/worker/assignment", assignment, cancellationToken);

    return await ToResultAsync("assign", response, cancellationToken);
});

// Called by AppHost or a developer, after assignment, to link the worker to the Host (PUT /admin/workers/{workerId}).
simulate.MapPost("/worker/link", async (IHttpClientFactory clients, CancellationToken cancellationToken) =>
{
    WorkerLink link = new(options.WorkerGrpcEndpoint.ToString());
    using HttpResponseMessage response = await clients.CreateClient(HostClient)
        .PutAsJsonAsync($"/admin/workers/{Uri.EscapeDataString(options.WorkerId)}", link, cancellationToken);

    return await ToResultAsync("link", response, cancellationToken);
});

// Called by a developer (browser, curl, .http file) or the AppHost health check to inspect the accepted state and
// push history.
simulate.MapGet("/host/state", () =>
{
    lock (gate)
    {
        return Results.Ok(new { accepted, pushes = history.ToArray() });
    }
});

// Called by a developer between test scenarios to reset the recorded state.
simulate.MapDelete("/host/state", () =>
{
    lock (gate)
    {
        history.Clear();
        accepted = null;
    }

    return Results.NoContent();
});

app.Run();

// Relays the downstream response so the caller sees exactly what the WorkerProxy or Host returned.
async Task<IResult> ToResultAsync(string operation, HttpResponseMessage response, CancellationToken cancellationToken)
{
    string body = await response.Content.ReadAsStringAsync(cancellationToken);
    logger.LogInformation("Worker {Operation} request to {Uri} -> {Status}", operation, response.RequestMessage?.RequestUri,
        (int)response.StatusCode);

    return Results.Content(body, response.Content.Headers.ContentType?.ToString(), statusCode: (int)response.StatusCode);
}

internal partial class Program
{
    private const string HostClient = "host";
    private const string WorkerProxyClient = "worker-proxy";

    private sealed class FakePlatformOptions
    {
        public required Uri HostUri { get; init; }

        public required Uri WorkerProxyUri { get; init; }

        public required Uri WorkerGrpcEndpoint { get; init; }

        public required string WorkerId { get; init; }
    }

    private sealed record WorkerAssignment(string StartupMode, string FunctionAppName, string FunctionGroupName,
        bool IsAlwaysReady, Dictionary<string, string> Environment, string FunctionAppDirectory);

    private sealed record WorkerLink(string WorkerGrpcEndpoint);

    private sealed record HostState(long SnapshotVersion, int LinkedWorkerCount, int LinkedHttpWorkerCount);

    private sealed record ReceivedPush(DateTimeOffset ReceivedAt, HostState State, bool Stale);
}
