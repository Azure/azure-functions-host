// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.Functions.WorkerProxy;

/// <summary>
/// Extracted handler methods for the worker proxy management API endpoints.
/// Each method contains the core business logic, separated from HTTP deserialization
/// so the logic can be unit-tested without an HTTP server.
/// </summary>
internal static class ManagementApiHandlers
{
    /// <summary>
    /// Handles <c>GET /admin/worker/ready</c>.
    /// </summary>
    public static IResult HandleReady(WorkerPodStateManager stateManager)
    {
        return stateManager.CurrentStatus == WorkerPodStatus.ReadyForRequest
            ? Results.Ok()
            : Results.StatusCode(503);
    }

    /// <summary>
    /// Handles <c>POST /admin/worker/assign</c> after the request body has been deserialized.
    /// </summary>
    public static async Task<IResult> HandleAssignAsync(
        WorkerAssignRequest? assignRequest,
        WorkerPodStateManager stateManager,
        FunctionRpcRelay relay,
        CancellationToken cancellationToken)
    {
        if (assignRequest is null)
        {
            return Results.BadRequest("Request body is required.");
        }

        var envVars = assignRequest.Environment ?? new Dictionary<string, string>();
        var functionAppDirectory = assignRequest.FunctionAppDirectory ?? "/home/site/wwwroot";

        stateManager.SetAssignMetadata(assignRequest.FunctionGroupName, assignRequest.IsAlwaysReady);

        try
        {
            await relay.SpecializeWorkerAsync(envVars, functionAppDirectory, cancellationToken);
            return Results.Ok();
        }
        catch (InvalidOperationException ex)
        {
            return Results.Conflict(ex.Message);
        }
        catch (OperationCanceledException)
        {
            return Results.StatusCode(504);
        }
    }

    /// <summary>
    /// Handles <c>POST /admin/worker/drain</c> after the request body has been deserialized.
    /// </summary>
    public static async Task<IResult> HandleDrainAsync(
        WorkerDrainRequest? drainRequest,
        WorkerPodStateManager stateManager,
        FunctionRpcRelay relay,
        ILogger logger)
    {
        if (drainRequest?.Reason is null)
        {
            return Results.BadRequest("Request body with a valid 'reason' is required.");
        }

        // Already draining — idempotent success without re-sending gRPC.
        if (stateManager.IsDraining)
        {
            return Results.Accepted();
        }

        // Notify the runtime first — only transition to Draining after the runtime
        // knows. Otherwise we'd stop expecting invocations while the runtime is still
        // routing them to us.
        try
        {
            await relay.SendDrainRequestToRuntimeAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send drain request to runtime over gRPC.");
            return Results.StatusCode(502);
        }

        stateManager.AcceptDrain(drainRequest.Reason.Value);
        return Results.Accepted();
    }

    /// <summary>
    /// Handles <c>POST /admin/infra/instanceState</c> after the poll revision has been extracted.
    /// </summary>
    public static async Task<IResult> HandleInstanceStateAsync(
        int clientRevision,
        WorkerPodStateManager stateManager,
        CancellationToken cancellationToken)
    {
        var result = await stateManager.WaitForChangeAsync(clientRevision, cancellationToken);

        if (result is null)
        {
            return Results.NoContent();
        }

        return Results.Ok(result);
    }
}
