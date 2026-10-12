// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.State;
using Microsoft.AspNetCore.Http;

namespace Azure.Functions.WorkerProxy.Management;

/// <summary>
/// Maps validated management requests and worker lifecycle outcomes to HTTP results.
/// </summary>
internal static class ManagementApiHandlers
{
    public static IResult GetWorkerReady(WorkerPodStateManager manager) =>
        manager.State.IsWorkerReady ? TypedResults.Ok() : TypedResults.StatusCode(StatusCodes.Status503ServiceUnavailable);

    public static IResult AssignWorker(WorkerAssignRequest request, WorkerPodStateManager manager)
    {
        if (!WorkerAssignRequestValidator.TryCreateAssignment(
            request, out WorkerAssignment? assignment, out IReadOnlyList<RequestValidationError> errors))
        {
            return ValidationError(errors);
        }

        return manager.Assign(assignment) switch
        {
            WorkerAssignmentResult.Created => TypedResults.Created("/admin/worker/assignment"),
            WorkerAssignmentResult.AlreadyAssigned => TypedResults.NoContent(),
            WorkerAssignmentResult.WorkerNotReady => Error(
                StatusCodes.Status503ServiceUnavailable, WorkerApiErrorCodes.WorkerNotReady, "The worker has not established a valid StartStream."),
            WorkerAssignmentResult.AssignmentConflict => Error(
                StatusCodes.Status409Conflict, WorkerApiErrorCodes.AssignmentConflict, "The pod is already assigned to a different assignment."),
            WorkerAssignmentResult.WorkerTerminated => Error(
                StatusCodes.Status409Conflict, WorkerApiErrorCodes.WorkerTerminated, "The assigned worker stream has terminated."),
            _ => throw new InvalidOperationException("Unexpected worker assignment result.")
        };
    }

    /// <summary>
    /// Parses the optional revision query and returns or polls worker state, honoring request cancellation.
    /// </summary>
    public static async Task<IResult> GetWorkerStateAsync(HttpRequest request, WorkerPodStateManager manager)
    {
        if (!TryGetLastKnownRevision(request.Query, out long? lastKnownRevision))
        {
            return InvalidRevision();
        }

        return await GetInstanceStateAsync(lastKnownRevision, manager, request.HttpContext.RequestAborted);
    }

    public static async Task<IResult> GetInstanceStateAsync(
        long? revision,
        WorkerPodStateManager manager,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (revision is not { } lastKnownRevision)
        {
            return StateResponse(manager.State);
        }

        // Revisions never decrease, so a revision valid here remains valid when the manager registers the poll.
        if (lastKnownRevision < 0 || lastKnownRevision > manager.State.Revision)
        {
            return InvalidRevision();
        }

        WorkerStatePollResult result = await manager.WaitForChangeAsync(lastKnownRevision, cancellationToken);
        return result.State is { } state ? StateResponse(state) : TypedResults.NoContent();
    }

    internal static IResult InvalidRevision() =>
        ValidationError([new(WorkerApiErrorCodes.InvalidRevision, "lastKnownRevision")]);

    private static bool TryGetLastKnownRevision(IQueryCollection query, out long? lastKnownRevision)
    {
        lastKnownRevision = null;
        if (!query.TryGetValue("lastKnownRevision", out var revisions))
        {
            return true;
        }

        if (revisions is not [string value])
        {
            return false;
        }

        if (!long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long revision))
        {
            return false;
        }

        lastKnownRevision = revision;
        return true;
    }

    private static IResult ValidationError(IReadOnlyList<RequestValidationError> errors) =>
        TypedResults.Json(new RequestValidationResponse(errors),
            WorkerProxyJsonContext.Default.RequestValidationResponse, statusCode: StatusCodes.Status400BadRequest);

    private static IResult Error(int statusCode, string code, string detail) =>
        TypedResults.Json(new WorkerApiErrorResponse(new(code, detail)),
            WorkerProxyJsonContext.Default.WorkerApiErrorResponse, statusCode: statusCode);

    private static IResult StateResponse(WorkerPodState state) =>
        TypedResults.Json(WorkerInstanceState.FromState(state), WorkerProxyJsonContext.Default.WorkerInstanceState);
}
