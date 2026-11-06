// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.Logging;
using Azure.Functions.WorkerProxy.State;
using Microsoft.AspNetCore.Http;

namespace Azure.Functions.WorkerProxy.Management;

/// <summary>
/// Maps validated management requests and worker lifecycle outcomes to HTTP results.
/// </summary>
internal static class ManagementApiHandlers
{
    public static IResult AssignWorker(WorkerAssignRequest request, WorkerPodStateManager manager, HttpContext? context = null)
    {
        if (!WorkerAssignRequestValidator.TryCreateAssignment(
            request, out WorkerAssignment? assignment, out IReadOnlyList<RequestValidationError> errors))
        {
            return ValidationError(errors);
        }

        WorkerAssignmentResult result = manager.Assign(assignment, out WorkerPodState state);
        context?.Features.Set(WorkerSystemLogContext.FromState(state));
        return result switch
        {
            WorkerAssignmentResult.Created => TypedResults.Created("/admin/worker/assignment"),
            WorkerAssignmentResult.AlreadyAssigned => TypedResults.NoContent(),
            WorkerAssignmentResult.WorkerNotReady => Error(
                StatusCodes.Status503ServiceUnavailable, WorkerApiErrorCodes.WorkerNotReady, "The worker pod is not accepting assignment."),
            WorkerAssignmentResult.AssignmentConflict => Error(
                StatusCodes.Status409Conflict, WorkerApiErrorCodes.AssignmentConflict, "The pod is already assigned to a different assignment."),
            WorkerAssignmentResult.WorkerTerminated => Error(
                StatusCodes.Status409Conflict, WorkerApiErrorCodes.WorkerTerminated, "The assigned worker stream has terminated."),
            _ => throw new InvalidOperationException("Unexpected worker assignment result.")
        };
    }

    /// <summary>
    /// Accepts the platform's last flat origin snapshot, or null for an immediate startup checkpoint.
    /// </summary>
    public static async Task<IResult> GetInstanceStateAsync(
        WorkerOriginState? originState,
        WorkerPodStateManager manager,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WorkerPodState current = manager.State;
        if (originState is null)
        {
            return StateResponse(current);
        }

        if (!string.Equals(originState.FunctionsContainerType, WorkerOriginState.ContainerType, StringComparison.Ordinal))
        {
            return ValidationError([new(WorkerApiErrorCodes.InvalidValue, "functionsContainerType")]);
        }

        int lastKnownRevision = originState.RevisionId;
        if (lastKnownRevision < 0)
        {
            return InvalidRevision();
        }

        if (originState != WorkerOriginState.FromState(current))
        {
            return StateResponse(current);
        }

        WorkerStatePollResult result = await manager.WaitForChangeAsync(lastKnownRevision, cancellationToken);
        return result.State is { } state ? StateResponse(state) : TypedResults.NoContent();
    }

    internal static IResult InvalidRevision() =>
        ValidationError([new(WorkerApiErrorCodes.InvalidRevision, "revisionId")]);

    private static IResult ValidationError(IReadOnlyList<RequestValidationError> errors) =>
        TypedResults.Json(new RequestValidationResponse(errors),
            WorkerProxyJsonContext.Default.RequestValidationResponse, statusCode: StatusCodes.Status400BadRequest);

    private static IResult Error(int statusCode, string code, string detail) =>
        TypedResults.Json(new WorkerApiErrorResponse(new(code, detail)),
            WorkerProxyJsonContext.Default.WorkerApiErrorResponse, statusCode: statusCode);

    private static IResult StateResponse(WorkerPodState state) =>
        TypedResults.Json(WorkerOriginState.FromState(state), WorkerProxyJsonContext.Default.WorkerOriginState);
}
