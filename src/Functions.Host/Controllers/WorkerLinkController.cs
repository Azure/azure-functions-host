// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Azure.Functions.Authentication;
using Azure.Functions.Host.Models;
using Azure.Functions.Host.WorkerLink;
using Azure.Functions.Rpc.Client;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using GrpcException = Grpc.Core.RpcException;
using WorkerRpcException = Microsoft.Azure.WebJobs.Script.Workers.Rpc.RpcException;

namespace Azure.Functions.Host.Controllers;

/// <summary>
/// Admin endpoint that links a worker to this runtime in compute separation mode.
/// </summary>
/// <remarks>
/// This controller exists only in compute separation mode. It is discovered via an MVC application part
/// registered from <c>ClientWorkerComposition</c>, so the <c>admin/workers/{workerId}</c> route is absent from the
/// standard host by construction.
/// </remarks>
public sealed partial class WorkerLinkController : Controller
{
    // Log-only reason for the 400 path. The response carries per-error codes instead, so this is not a wire code.
    private const string ValidationFailedReason = "ValidationFailed";

    private readonly ILogger<WorkerLinkController> _logger;
    private readonly IWorkerChannelRegistry _registry;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkerLinkController"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="registry">The registry that owns worker channels and link admission.</param>
    public WorkerLinkController(
        ILogger<WorkerLinkController> logger, IWorkerChannelRegistry registry)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <summary>
    /// Links one worker to this runtime.
    /// </summary>
    /// <param name="workerId">
    /// The worker ID from the request path. The runtime treats it as an opaque, case-sensitive identifier and rejects
    /// null, empty, or all-whitespace values.
    /// </param>
    /// <param name="request">The worker link request body.</param>
    /// <param name="cancellationToken">Cancels this request and, for a new link, its initialization attempt.</param>
    /// <returns>
    /// <c>201</c> for a newly created link or <c>200</c> for a matching retry, after initialization and registration.
    /// Returns <c>400</c> with a validation errors envelope, <c>409</c> when the identity is already linked to a
    /// different endpoint, credentials, or Runtime identity, or its channel has terminated, or <c>503</c> when the runtime is stopping or the worker
    /// connection or handshake is unavailable or times out.
    /// Link failures return an error envelope with a stable code. No Location header is returned.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Success carries no response body: the status code is the entire success contract, and the request URI already
    /// identifies the link. Clients must treat any success body as optional so that fields can be added later without
    /// a breaking change.
    /// </para>
    /// <para>
    /// The platform must complete WorkerProxy assignment before invoking this endpoint. Assignment can precede
    /// the Worker's valid <c>StartStream</c>; this action waits for startup and initialization rather than treating
    /// assignment acceptance as readiness. Linking establishes the Runtime stream used for <c>WorkerInitRequest</c>.
    /// This action does not guard against pre-assignment links.
    /// </para>
    /// <para>
    /// Retry an uncertain outcome with the same case-sensitive worker ID, ordinal <c>Uri.AbsoluteUri</c> endpoint,
    /// credentials, and Runtime identity values. All declared identity fields participate in value equality with ordinal string
    /// comparison; omitted and null identity are equivalent, but differ from a supplied identity. Unknown identity
    /// fields are ignored. Matching pending requests share initialization; they do not create another connection.
    /// A successful retry returns <c>200</c>. Changing the endpoint, credentials, or identity returns <c>409</c>/<c>LinkConflict</c>.
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <c>400</c>: the <c>errors</c> validation envelope reports <c>Required</c>, <c>InvalidEndpoint</c>, or
    /// <c>InvalidBody</c> with a field target. Correct the request before retrying. Unsupported media types use
    /// framework <c>415</c> handling, which does not guarantee this envelope.
    /// </description></item>
    /// <item><description>
    /// <c>409</c>: <c>LinkConflict</c> identifies conflicting endpoint, credentials, or Runtime identity;
    /// <c>WorkerTerminated</c> identifies an initialized channel that has terminated. Neither is resolved by replaying
    /// the same request; a terminated worker ID cannot be linked again in this Runtime registry.
    /// </description></item>
    /// <item><description>
    /// <c>503</c>: <c>RuntimeStopping</c>, <c>WorkerUnavailable</c>, or <c>LinkTimeout</c>.
    /// Retry transient connection/initialization failures after cleanup, using the same values and platform retry
    /// policy. A timeout does not establish whether a shared attempt completed; an exact retry can discover success.
    /// A stopping Runtime or an unhealthy WorkerProxy requires platform recovery rather than indefinite retries.
    /// </description></item>
    /// </list>
    /// <para>
    /// Lifecycle failure codes are returned in <c>error.code</c>, with diagnostic text in <c>error.detail</c>.
    /// Callers should branch on the stable code, not the detail.
    /// </para>
    /// </remarks>
    [HttpPut]
    [Route("admin/workers/{workerId}")]
    [Authorize(Policy = ContainerManagementAuthenticationHandler.SchemeName)]
    public async Task<IActionResult> LinkWorker(
        [FromRoute] string workerId,
        [FromBody] WorkerLinkRequest? request,
        CancellationToken cancellationToken = default)
    {
        long started = Stopwatch.GetTimestamp();
        List<RequestValidationError> errors = ValidateRequest(workerId, request, out Uri? grpcEndpoint);
        if (errors.Count > 0 || grpcEndpoint is null)
        {
            Log.LinkRejected(_logger, null, workerId, ValidationFailedReason,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);

            return BadRequest(new RequestValidationResponse(errors));
        }

        return await LinkValidatedWorkerAsync(
            workerId, grpcEndpoint, request!.RuntimeAccessKey!, request.RuntimeIdentity, started, cancellationToken);
    }

    private List<RequestValidationError> ValidateRequest(
        string workerId, WorkerLinkRequest? request, out Uri? grpcEndpoint)
    {
        grpcEndpoint = null;
        List<RequestValidationError> errors = [];

        if (string.IsNullOrWhiteSpace(workerId))
        {
            errors.Add(new(RequestValidationErrorCodes.Required, "workerId"));

            return errors;
        }

        if (!ModelState.IsValid || request is null)
        {
            errors.Add(new(RequestValidationErrorCodes.InvalidBody, "request"));

            return errors;
        }

        if (!RuntimeWorkerAuthentication.TryCreateHttpsOrigin(request.WorkerGrpcEndpoint, out grpcEndpoint))
        {
            errors.Add(string.IsNullOrWhiteSpace(request.WorkerGrpcEndpoint)
                ? new(RequestValidationErrorCodes.Required, "workerGrpcEndpoint")
                : new(RequestValidationErrorCodes.InvalidEndpoint, "workerGrpcEndpoint"));
        }

        if (string.IsNullOrWhiteSpace(request.RuntimeAccessKey))
        {
            errors.Add(new(RequestValidationErrorCodes.Required, "runtimeAccessKey"));
        }

        return errors;
    }

    // Wait for initialization and registration, then map the outcome to an HTTP response.
    private async Task<IActionResult> LinkValidatedWorkerAsync(
        string workerId, Uri grpcEndpoint, string runtimeAccessKey, WorkerLinkRuntimeIdentity? runtimeIdentity,
        long started, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            WorkerLinkResult result = await _registry.LinkAsync(workerId, grpcEndpoint, runtimeAccessKey, cancellationToken, runtimeIdentity);
            Log.LinkAccepted(_logger, workerId, Stopwatch.GetElapsedTime(started).TotalMilliseconds);

            return StatusCode(result.IsNewLink ? StatusCodes.Status201Created : StatusCodes.Status200OK);
        }
        catch (WorkerLinkException exception) when (exception.Reason is WorkerLinkFailureReason.InvalidRuntimeAccessKey)
        {
            Log.LinkRejected(_logger, null, workerId, ValidationFailedReason,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);

            return BadRequest(new RequestValidationResponse([new(RequestValidationErrorCodes.InvalidValue, "runtimeAccessKey")]));
        }
        catch (WorkerLinkException exception)
        {
            return CreateLinkFailureResponse(workerId, exception.Reason, started, exception);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Log.LinkCanceled(_logger, workerId, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
        catch (ObjectDisposedException exception)
        {
            return CreateLinkFailureResponse(workerId, WorkerLinkFailureReason.RuntimeStopping, started, exception);
        }
        catch (Exception exception) when (exception is TimeoutException or GrpcException { StatusCode: Grpc.Core.StatusCode.DeadlineExceeded })
        {
            return CreateLinkFailureResponse(workerId, WorkerLinkFailureReason.Timeout, started, exception);
        }
        catch (Exception exception) when (exception is GrpcException or WorkerRpcException or HttpRequestException or
            IOException or SocketException or ChannelClosedException or OperationCanceledException or UriFormatException)
        {
            return CreateLinkFailureResponse(workerId, WorkerLinkFailureReason.Unavailable, started, exception);
        }
    }

    private ObjectResult CreateLinkFailureResponse(
        string workerId, WorkerLinkFailureReason reason, long started, Exception exception)
    {
        (int statusCode, string code, string detail) = reason switch
        {
            WorkerLinkFailureReason.Conflict => (StatusCodes.Status409Conflict, ErrorCodes.LinkConflict,
                "The worker identity is already linked with different endpoints or credentials."),
            WorkerLinkFailureReason.WorkerTerminated => (StatusCodes.Status409Conflict, ErrorCodes.WorkerTerminated,
                "The worker's initialized channel has terminated."),
            WorkerLinkFailureReason.RuntimeStopping => (StatusCodes.Status503ServiceUnavailable, ErrorCodes.RuntimeStopping,
                "The runtime is stopping."),
            WorkerLinkFailureReason.Unavailable => (StatusCodes.Status503ServiceUnavailable, ErrorCodes.WorkerUnavailable,
                "The worker connection or initialization handshake was unavailable."),
            WorkerLinkFailureReason.Timeout => (StatusCodes.Status503ServiceUnavailable, ErrorCodes.LinkTimeout,
                "The worker connection or initialization handshake timed out."),
            WorkerLinkFailureReason.AuthenticationUnavailable => (StatusCodes.Status503ServiceUnavailable, ErrorCodes.AuthenticationUnavailable,
                "The runtime application authentication configuration is unavailable."),
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown worker link failure reason."),
        };

        Log.LinkRejected(_logger, exception, workerId, code, Stopwatch.GetElapsedTime(started).TotalMilliseconds);

        return StatusCode(statusCode, new WorkerLinkErrorResponse(new WorkerLinkError(code, detail)));
    }

    // Stable wire codes. Tests assert the literal strings so a rename here cannot pass silently.
    private static class ErrorCodes
    {
        public const string AuthenticationUnavailable = nameof(AuthenticationUnavailable);
        public const string LinkConflict = nameof(LinkConflict);
        public const string LinkTimeout = nameof(LinkTimeout);
        public const string RuntimeStopping = nameof(RuntimeStopping);
        public const string WorkerTerminated = nameof(WorkerTerminated);
        public const string WorkerUnavailable = nameof(WorkerUnavailable);
    }

    private static partial class Log
    {
        // Worker management controllers share the 700-799 event ID range.

        [LoggerMessage(700, LogLevel.Information, "Worker link accepted for {workerId} after {elapsedMilliseconds} ms.")]
        public static partial void LinkAccepted(ILogger logger, string workerId, double elapsedMilliseconds);

        [LoggerMessage(701, LogLevel.Warning, "Worker link rejected for {workerId}: {reason}, after {elapsedMilliseconds} ms.")]
        public static partial void LinkRejected(ILogger logger, Exception? exception, string? workerId, string reason, double elapsedMilliseconds);

        [LoggerMessage(702, LogLevel.Information, "Worker link canceled for {workerId} after {elapsedMilliseconds} ms.")]
        public static partial void LinkCanceled(ILogger logger, string workerId, double elapsedMilliseconds);
    }
}
