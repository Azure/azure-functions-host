// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Azure.Functions.Host.Models;
using Azure.Functions.Host.WorkerLink;
using Azure.Functions.Rpc.Client;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using GrpcException = Grpc.Core.RpcException;
using WorkerRpcException = Microsoft.Azure.WebJobs.Script.Workers.Rpc.RpcException;

namespace Azure.Functions.Host.Controllers;

/// <summary>
/// Compute-only admin endpoint that links a worker pod to this runtime.
/// </summary>
/// <remarks>
/// This controller exists only in the compute product. It is discovered via an MVC application part
/// registered from <c>ClientWorkerComposition</c>, so the <c>admin/workers/{workerPodName}</c> route is absent from the
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
    public WorkerLinkController(ILogger<WorkerLinkController> logger, IWorkerChannelRegistry registry)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <summary>
    /// Links one worker pod to this runtime.
    /// </summary>
    /// <param name="workerPodName">
    /// The worker pod identity from the request path. Null, empty, or all-whitespace values are rejected.
    /// </param>
    /// <param name="request">The worker link request body.</param>
    /// <param name="cancellationToken">Cancels this request and, for a new link, its initialization attempt.</param>
    /// <returns>
    /// <c>201</c> for a newly created link or <c>200</c> for a matching retry, after initialization and registration.
    /// Returns <c>400</c> with a validation errors envelope, <c>409</c> when the identity is already linked to a
    /// different endpoint or its channel has terminated, or <c>503</c> when the runtime is stopping or the worker
    /// connection or handshake is unavailable or times out.
    /// Link failures return an error envelope with a stable code. No Location header is returned.
    /// </returns>
    /// <remarks>
    /// Success carries no response body: the status code is the entire success contract, and the request URI already
    /// identifies the link. Clients must treat any success body as optional so that fields can be added later without
    /// a breaking change.
    /// </remarks>
    [HttpPut]
    [Route("admin/workers/{workerPodName}")]
    // [Authorize(Policy = PolicyNames.AdminAuthLevel)]
    public async Task<IActionResult> LinkWorker([FromRoute] string workerPodName, [FromBody] WorkerLinkRequest? request,
        CancellationToken cancellationToken = default)
    {
        long started = Stopwatch.GetTimestamp();
        IReadOnlyList<RequestValidationError> errors = ValidateRequest(workerPodName, request, out Uri? grpcEndpoint);
        if (errors.Count > 0 || grpcEndpoint is null)
        {
            Log.LinkRejected(_logger, null, workerPodName, ValidationFailedReason,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);

            return BadRequest(new RequestValidationResponse(errors));
        }

        return await LinkValidatedWorkerAsync(workerPodName, grpcEndpoint, started, cancellationToken);
    }

    private IReadOnlyList<RequestValidationError> ValidateRequest(string workerPodName, WorkerLinkRequest? request,
        out Uri? grpcEndpoint)
    {
        grpcEndpoint = null;
        List<RequestValidationError> errors = [];
        if (string.IsNullOrWhiteSpace(workerPodName))
        {
            errors.Add(new(ErrorCodes.Required, "workerPodName"));

            return errors;
        }

        if (!ModelState.IsValid || request is null)
        {
            errors.Add(new(ErrorCodes.InvalidBody, "request"));

            return errors;
        }

        if (!TryParseEndpoint(request.WorkerGrpcEndpoint, out grpcEndpoint))
        {
            errors.Add(string.IsNullOrWhiteSpace(request.WorkerGrpcEndpoint)
                ? new(ErrorCodes.Required, "workerGrpcEndpoint")
                : new(ErrorCodes.InvalidEndpoint, "workerGrpcEndpoint"));
        }

        return errors;
    }

    // Wait for initialization and registration, then map the outcome to an HTTP response.
    private async Task<IActionResult> LinkValidatedWorkerAsync(string workerPodName, Uri grpcEndpoint, long started,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            WorkerLinkResult result = await _registry.LinkAsync(workerPodName, grpcEndpoint, cancellationToken);
            Log.LinkAccepted(_logger, workerPodName, Stopwatch.GetElapsedTime(started).TotalMilliseconds);

            return StatusCode(result.IsNewLink ? StatusCodes.Status201Created : StatusCodes.Status200OK);
        }
        catch (WorkerLinkException exception)
        {
            return CreateLinkFailureResponse(workerPodName, exception.Reason, started, exception);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Log.LinkCanceled(_logger, workerPodName, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
        catch (ObjectDisposedException exception)
        {
            return CreateLinkFailureResponse(workerPodName, WorkerLinkFailureReason.RuntimeStopping, started, exception);
        }
        catch (Exception exception) when (exception is TimeoutException or GrpcException { StatusCode: Grpc.Core.StatusCode.DeadlineExceeded })
        {
            return CreateLinkFailureResponse(workerPodName, WorkerLinkFailureReason.Timeout, started, exception);
        }
        catch (Exception exception) when (exception is GrpcException or WorkerRpcException or HttpRequestException or
            IOException or SocketException or ChannelClosedException or OperationCanceledException or UriFormatException)
        {
            return CreateLinkFailureResponse(workerPodName, WorkerLinkFailureReason.Unavailable, started, exception);
        }
    }

    private ObjectResult CreateLinkFailureResponse(string workerPodName, WorkerLinkFailureReason reason,
        long started, Exception exception)
    {
        (int statusCode, string code, string detail) = reason switch
        {
            WorkerLinkFailureReason.Conflict => (StatusCodes.Status409Conflict, ErrorCodes.LinkConflict,
                "The worker identity is already linked to another endpoint."),
            WorkerLinkFailureReason.WorkerTerminated => (StatusCodes.Status409Conflict, ErrorCodes.WorkerTerminated,
                "The worker's initialized channel has terminated."),
            WorkerLinkFailureReason.RuntimeStopping => (StatusCodes.Status503ServiceUnavailable, ErrorCodes.RuntimeStopping,
                "The runtime is stopping."),
            WorkerLinkFailureReason.Unavailable => (StatusCodes.Status503ServiceUnavailable, ErrorCodes.WorkerUnavailable,
                "The worker connection or initialization handshake was unavailable."),
            WorkerLinkFailureReason.Timeout => (StatusCodes.Status503ServiceUnavailable, ErrorCodes.LinkTimeout,
                "The worker connection or initialization handshake timed out."),
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown worker link failure reason."),
        };

        Log.LinkRejected(_logger, exception, workerPodName, code, Stopwatch.GetElapsedTime(started).TotalMilliseconds);

        return StatusCode(statusCode, new WorkerLinkErrorResponse(new WorkerLinkError(code, detail)));
    }

    // Accept only HTTP(S) authorities without credentials, non-root paths, queries, or fragments.
    private static bool TryParseEndpoint(string? value, [NotNullWhen(true)] out Uri? endpoint)
        => Uri.TryCreate(value, UriKind.Absolute, out endpoint) &&
            (string.Equals(endpoint.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) &&
            !string.IsNullOrWhiteSpace(endpoint.Host) &&
            string.IsNullOrEmpty(endpoint.UserInfo) &&
            string.Equals(endpoint.AbsolutePath, "/", StringComparison.Ordinal) &&
            string.IsNullOrEmpty(endpoint.Query) &&
            string.IsNullOrEmpty(endpoint.Fragment);

    // Stable wire codes. Tests assert the literal strings so a rename here cannot pass silently.
    private static class ErrorCodes
    {
        public const string InvalidBody = nameof(InvalidBody);
        public const string InvalidEndpoint = nameof(InvalidEndpoint);
        public const string LinkConflict = nameof(LinkConflict);
        public const string LinkTimeout = nameof(LinkTimeout);
        public const string Required = nameof(Required);
        public const string RuntimeStopping = nameof(RuntimeStopping);
        public const string WorkerTerminated = nameof(WorkerTerminated);
        public const string WorkerUnavailable = nameof(WorkerUnavailable);
    }

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Information, "Worker link accepted for {workerId} after {elapsedMilliseconds} ms.")]
        public static partial void LinkAccepted(ILogger logger, string workerId, double elapsedMilliseconds);

        [LoggerMessage(1, LogLevel.Warning, "Worker link rejected for {workerId}: {reason}, after {elapsedMilliseconds} ms.")]
        public static partial void LinkRejected(ILogger logger, Exception? exception, string? workerId, string reason, double elapsedMilliseconds);

        [LoggerMessage(2, LogLevel.Information, "Worker link canceled for {workerId} after {elapsedMilliseconds} ms.")]
        public static partial void LinkCanceled(ILogger logger, string workerId, double elapsedMilliseconds);
    }
}
