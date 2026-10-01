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

    // Bounds how long a link that arrives before specialization holds its request.
    private static readonly TimeSpan DefaultSpecializedConfigurationTimeout = TimeSpan.FromSeconds(10);

    private readonly ILogger<WorkerLinkController> _logger;
    private readonly IWorkerChannelRegistry _registry;
    private readonly WorkerLinkConfigurationMonitor _configurationMonitor;
    private readonly TimeSpan _specializedConfigurationTimeout;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkerLinkController"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="registry">The registry that owns worker channels and link admission.</param>
    /// <param name="configurationMonitor">Observes when the specialized application configuration becomes available.</param>
    public WorkerLinkController(
        ILogger<WorkerLinkController> logger,
        IWorkerChannelRegistry registry,
        WorkerLinkConfigurationMonitor configurationMonitor)
        : this(logger, registry, configurationMonitor, DefaultSpecializedConfigurationTimeout)
    {
    }

    internal WorkerLinkController(
        ILogger<WorkerLinkController> logger,
        IWorkerChannelRegistry registry,
        WorkerLinkConfigurationMonitor configurationMonitor,
        TimeSpan specializedConfigurationTimeout)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _configurationMonitor = configurationMonitor ?? throw new ArgumentNullException(nameof(configurationMonitor));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(specializedConfigurationTimeout, TimeSpan.Zero);
        _specializedConfigurationTimeout = specializedConfigurationTimeout;
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
    /// different endpoint or its channel has terminated, or <c>503</c> when the runtime is stopping, has not been
    /// specialized, or the worker connection or handshake is unavailable or times out.
    /// Link failures return an error envelope with a stable code. No Location header is returned.
    /// </returns>
    /// <remarks>
    /// Success carries no response body: the status code is the entire success contract, and the request URI already
    /// identifies the link. Clients must treat any success body as optional so that fields can be added later without
    /// a breaking change.
    /// </remarks>
    [HttpPut]
    [Route("admin/workers/{workerId}")]
    // [Authorize(Policy = PolicyNames.AdminAuthLevel)]
    public async Task<IActionResult> LinkWorker([FromRoute] string workerId, [FromBody] WorkerLinkRequest? request,
        CancellationToken cancellationToken = default)
    {
        long started = Stopwatch.GetTimestamp();
        IReadOnlyList<RequestValidationError> errors = ValidateRequest(workerId, request, out Uri? grpcEndpoint);
        if (errors.Count > 0 || grpcEndpoint is null)
        {
            Log.LinkRejected(_logger, null, workerId, ValidationFailedReason,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);

            return BadRequest(new RequestValidationResponse(errors));
        }

        return await LinkValidatedWorkerAsync(workerId, grpcEndpoint, started, cancellationToken);
    }

    private IReadOnlyList<RequestValidationError> ValidateRequest(string workerId, WorkerLinkRequest? request,
        out Uri? grpcEndpoint)
    {
        grpcEndpoint = null;
        List<RequestValidationError> errors = [];

        if (string.IsNullOrWhiteSpace(workerId))
        {
            errors.Add(new(ErrorCodes.Required, "workerId"));

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
    private async Task<IActionResult> LinkValidatedWorkerAsync(string workerId, Uri grpcEndpoint, long started,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!await WaitForSpecializedConfigurationAsync(workerId, cancellationToken))
            {
                return CreateLinkFailureResponse(workerId, StatusCodes.Status503ServiceUnavailable,
                    ErrorCodes.RuntimeNotSpecialized, "The runtime has not been specialized.", started, exception: null);
            }

            WorkerLinkResult result = await _registry.LinkAsync(workerId, grpcEndpoint, cancellationToken);
            Log.LinkAccepted(_logger, workerId, Stopwatch.GetElapsedTime(started).TotalMilliseconds);

            return StatusCode(result.IsNewLink ? StatusCodes.Status201Created : StatusCodes.Status200OK);
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

    // Returns false when this wait times out. Request cancellation propagates, so an aborted request is not a timeout.
    private async Task<bool> WaitForSpecializedConfigurationAsync(string workerId, CancellationToken cancellationToken)
    {
        // Canceling the monitor's wait, rather than abandoning it, also removes its change registration.
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_specializedConfigurationTimeout);
        Task wait = _configurationMonitor.WaitForSpecializedConfigurationAsync(timeout.Token);

        if (!wait.IsCompleted)
        {
            Log.WaitingForSpecializedConfiguration(_logger, workerId);
        }

        try
        {
            await wait;

            return true;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private ObjectResult CreateLinkFailureResponse(string workerId, WorkerLinkFailureReason reason,
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

        return CreateLinkFailureResponse(workerId, statusCode, code, detail, started, exception);
    }

    private ObjectResult CreateLinkFailureResponse(string workerId, int statusCode, string code, string detail,
        long started, Exception? exception)
    {
        Log.LinkRejected(_logger, exception, workerId, code, Stopwatch.GetElapsedTime(started).TotalMilliseconds);

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
        public const string RuntimeNotSpecialized = nameof(RuntimeNotSpecialized);
        public const string RuntimeStopping = nameof(RuntimeStopping);
        public const string WorkerTerminated = nameof(WorkerTerminated);
        public const string WorkerUnavailable = nameof(WorkerUnavailable);
    }

    private static partial class Log
    {
        // EventId range is 700-799

        [LoggerMessage(700, LogLevel.Information, "Worker link accepted for {workerId} after {elapsedMilliseconds} ms.")]
        public static partial void LinkAccepted(ILogger logger, string workerId, double elapsedMilliseconds);

        [LoggerMessage(701, LogLevel.Warning, "Worker link rejected for {workerId}: {reason}, after {elapsedMilliseconds} ms.")]
        public static partial void LinkRejected(ILogger logger, Exception? exception, string? workerId, string reason, double elapsedMilliseconds);

        [LoggerMessage(702, LogLevel.Information, "Worker link canceled for {workerId} after {elapsedMilliseconds} ms.")]
        public static partial void LinkCanceled(ILogger logger, string workerId, double elapsedMilliseconds);

        [LoggerMessage(703, LogLevel.Debug, "Worker link for {workerId} is waiting for the specialized configuration.")]
        public static partial void WaitingForSpecializedConfiguration(ILogger logger, string workerId);
    }
}
