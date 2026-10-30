// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
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
            WorkerAssignmentResult.AssignmentConflict => Error(
                StatusCodes.Status409Conflict, WorkerApiErrorCodes.AssignmentConflict, "The pod is already assigned to a different assignment."),
            WorkerAssignmentResult.WorkerTerminated => Error(
                StatusCodes.Status409Conflict, WorkerApiErrorCodes.WorkerTerminated, "The assigned worker stream has terminated."),
            _ => throw new InvalidOperationException("Unexpected worker assignment result.")
        };
    }

    /// <summary>
    /// Accepts the platform's last flat origin snapshot, or null on the initial availability poll.
    /// </summary>
    public static async Task<IResult> GetInstanceStateAsync(
        WorkerInstanceState? originState,
        WorkerPodStateManager manager,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WorkerPodState current = manager.State;
        if (originState is null)
        {
            return StateResponse(current, manager.StartupTime);
        }

        if (!string.Equals(originState.FunctionsContainerType, WorkerInstanceState.ContainerType, StringComparison.Ordinal))
        {
            return ValidationError([new(WorkerApiErrorCodes.InvalidValue, "functionsContainerType")]);
        }

        int lastKnownRevision = originState.RevisionId;
        if (lastKnownRevision < 0)
        {
            return InvalidRevision();
        }

        if (current.AssignmentState is WorkerAssignmentState.Unassigned
            || !originState.MatchesPublishedState(WorkerInstanceState.FromState(current, manager.StartupTime)))
        {
            return StateResponse(current, manager.StartupTime);
        }

        WorkerStatePollResult result = await manager.WaitForChangeAsync(lastKnownRevision, cancellationToken);
        return result.State is { } state ? StateResponse(state, manager.StartupTime) : TypedResults.NoContent();
    }

    internal static IResult InvalidRevision() =>
        ValidationError([new(WorkerApiErrorCodes.InvalidRevision, "revisionId")]);

    private static IResult ValidationError(IReadOnlyList<RequestValidationError> errors) =>
        TypedResults.Json(new RequestValidationResponse(errors),
            WorkerProxyJsonContext.Default.RequestValidationResponse, statusCode: StatusCodes.Status400BadRequest);

    private static IResult Error(int statusCode, string code, string detail) =>
        TypedResults.Json(new WorkerApiErrorResponse(new(code, detail)),
            WorkerProxyJsonContext.Default.WorkerApiErrorResponse, statusCode: statusCode);

    private static IResult StateResponse(WorkerPodState state, DateTimeOffset startupTime) =>
        state.AssignmentState is WorkerAssignmentState.Unassigned
            ? TypedResults.NoContent()
            : TypedResults.Json(WorkerInstanceState.FromState(state, startupTime), WorkerProxyJsonContext.Default.WorkerInstanceState);
}
