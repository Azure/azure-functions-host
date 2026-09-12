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
    public static IResult GetWorkerReady(WorkerPodStateManager manager) =>
        manager.State.IsWorkerReady ? TypedResults.Ok() : TypedResults.StatusCode(StatusCodes.Status503ServiceUnavailable);

    public static IResult AssignWorker(WorkerAssignRequest? request, WorkerPodStateManager manager)
    {
        if (request is null)
        {
            return InvalidBody();
        }

        List<RequestValidationError> errors = [];
        if (string.IsNullOrWhiteSpace(request.FunctionAppName))
        {
            errors.Add(new(WorkerApiErrorCodes.Required, "functionAppName"));
        }

        if (string.IsNullOrWhiteSpace(request.FunctionGroupName))
        {
            errors.Add(new(WorkerApiErrorCodes.Required, "functionGroupName"));
        }

        if (request.IsAlwaysReady is null)
        {
            errors.Add(new(WorkerApiErrorCodes.Required, "isAlwaysReady"));
        }

        if (string.IsNullOrWhiteSpace(request.FunctionAppDirectory))
        {
            errors.Add(new(WorkerApiErrorCodes.Required, "functionAppDirectory"));
        }

        Dictionary<string, string> environment = new(StringComparer.Ordinal);
        if (request.Environment is null)
        {
            errors.Add(new(WorkerApiErrorCodes.Required, "environment"));
        }
        else
        {
            foreach ((string key, string? value) in request.Environment)
            {
                if (string.IsNullOrEmpty(key) || value is null)
                {
                    // Report the invalid field once without exposing environment keys or values.
                    errors.Add(new(WorkerApiErrorCodes.InvalidValue, "environment"));
                    break;
                }

                environment.Add(key, value);
            }
        }

        if (errors.Count > 0
            || request.FunctionAppName is not { } functionAppName
            || request.FunctionGroupName is not { } functionGroupName
            || request.FunctionAppDirectory is not { } functionAppDirectory
            || request.IsAlwaysReady is not { } isAlwaysReady)
        {
            return ValidationError(errors);
        }

        WorkerAssignment assignment = new(
            functionAppName,
            functionGroupName,
            isAlwaysReady,
            environment,
            functionAppDirectory);
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

    internal static IResult InvalidBody() =>
        ValidationError([new(WorkerApiErrorCodes.InvalidBody, "request")]);

    internal static IResult InvalidRevision() =>
        ValidationError([new(WorkerApiErrorCodes.InvalidRevision, "lastKnownRevision")]);

    private static IResult ValidationError(IReadOnlyList<RequestValidationError> errors) =>
        TypedResults.Json(new RequestValidationResponse(errors),
            WorkerProxyJsonContext.Default.RequestValidationResponse, statusCode: StatusCodes.Status400BadRequest);

    private static IResult Error(int statusCode, string code, string detail) =>
        TypedResults.Json(new WorkerApiErrorResponse(new(code, detail)),
            WorkerProxyJsonContext.Default.WorkerApiErrorResponse, statusCode: statusCode);

    private static IResult StateResponse(WorkerPodState state) =>
        TypedResults.Json(WorkerInstanceState.FromState(state), WorkerProxyJsonContext.Default.WorkerInstanceState);
}
