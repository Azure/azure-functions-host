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
/// registered from <c>ClientWorkerComposition</c>, so the <c>admin/workers</c> route is absent from the
/// standard host by construction.
/// </remarks>
public sealed partial class WorkerLinkController : Controller
{
    private readonly ILogger<WorkerLinkController> _logger;
    private readonly IWorkerChannelRegistry _registry;
    private readonly TimeSpan _linkTimeout;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkerLinkController"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="registry">The registry that owns worker channels and link admission.</param>
    public WorkerLinkController(ILogger<WorkerLinkController> logger, IWorkerChannelRegistry registry)
        : this(logger, registry, TimeSpan.FromSeconds(30))
    {
    }

    internal WorkerLinkController(ILogger<WorkerLinkController> logger, IWorkerChannelRegistry registry, TimeSpan linkTimeout)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(linkTimeout, TimeSpan.Zero);
        _linkTimeout = linkTimeout;
    }

    /// <summary>
    /// Links one worker pod to this runtime.
    /// </summary>
    /// <param name="request">The worker link request body.</param>
    /// <param name="cancellationToken">Cancels this request and, for a new link, its initialization attempt.</param>
    /// <returns>
    /// <c>200</c> after initialization, <c>400</c> with a validation errors envelope, <c>409</c> for rejected admission,
    /// or <c>503</c> when the worker connection or handshake is unavailable.
    /// </returns>
    [HttpPut]
    [Route("admin/workers")]
    // [Authorize(Policy = PolicyNames.AdminAuthLevel)]
    public async Task<IActionResult> LinkWorker([FromBody] WorkerLinkRequest? request, CancellationToken cancellationToken = default)
    {
        long started = Stopwatch.GetTimestamp();
        IReadOnlyList<RequestValidationError> errors = ValidateRequest(request, out Uri? grpcEndpoint);
        if (errors.Count > 0 || grpcEndpoint is null || request?.WorkerPodName is null)
        {
            Log.LinkRejected(_logger, null, request?.WorkerPodName, "ValidationFailed",
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);

            return BadRequest(new RequestValidationResponse(errors));
        }

        return await LinkValidatedWorkerAsync(request.WorkerPodName, grpcEndpoint, started, cancellationToken);
    }

    private IReadOnlyList<RequestValidationError> ValidateRequest(WorkerLinkRequest? request, out Uri? grpcEndpoint)
    {
        grpcEndpoint = null;
        List<RequestValidationError> errors = [];
        if (!ModelState.IsValid || request is null)
        {
            errors.Add(new("InvalidBody", "request"));

            return errors;
        }

        if (string.IsNullOrWhiteSpace(request.WorkerPodName))
        {
            errors.Add(new("Required", "workerPodName"));
        }

        if (!TryParseEndpoint(request.WorkerGrpcEndpoint, out grpcEndpoint))
        {
            errors.Add(string.IsNullOrWhiteSpace(request.WorkerGrpcEndpoint)
                ? new("Required", "workerGrpcEndpoint")
                : new("InvalidEndpoint", "workerGrpcEndpoint"));
        }

        if (!string.IsNullOrWhiteSpace(request.WorkerHttpEndpoint) && !TryParseEndpoint(request.WorkerHttpEndpoint, out _))
        {
            errors.Add(new("InvalidEndpoint", "workerHttpEndpoint"));
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
            using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Task link = _registry.LinkAsync(workerPodName, grpcEndpoint, deadline.Token);
            if (!link.IsCompleted)
            {
                deadline.CancelAfter(_linkTimeout);
            }

            await link;
            Log.LinkAccepted(_logger, workerPodName, Stopwatch.GetElapsedTime(started).TotalMilliseconds);

            return Ok(new WorkerLinkResponse
            {
                WorkerPodName = workerPodName,
                Status = WorkerLinkStatus.Linked,
            });
        }
        catch (WorkerLinkException exception)
        {
            return CreateLinkFailureResponse(workerPodName, exception.Reason, exception.Message, started, exception);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Log.LinkCanceled(_logger, workerPodName, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
        catch (ObjectDisposedException exception)
        {
            return CreateLinkFailureResponse(workerPodName, WorkerLinkFailureReason.RuntimeStopping,
                "The runtime is stopping.", started, exception);
        }
        catch (Exception exception) when (exception is GrpcException or WorkerRpcException or HttpRequestException or
            IOException or SocketException or TimeoutException or ChannelClosedException or OperationCanceledException or UriFormatException)
        {
            return CreateLinkFailureResponse(workerPodName, WorkerLinkFailureReason.Unavailable,
                "The worker connection or initialization handshake was unavailable.", started, exception);
        }
    }

    private ObjectResult CreateLinkFailureResponse(string workerPodName, WorkerLinkFailureReason reason, string detail,
        long started, Exception exception)
    {
        Log.LinkRejected(_logger, exception, workerPodName, reason.ToString(),
            Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        int statusCode = reason is WorkerLinkFailureReason.Unavailable
            ? StatusCodes.Status503ServiceUnavailable
            : StatusCodes.Status409Conflict;

        return StatusCode(statusCode, new WorkerLinkResponse
        {
            WorkerPodName = workerPodName,
            Status = WorkerLinkStatus.LinkFailed,
            Detail = detail,
        });
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
