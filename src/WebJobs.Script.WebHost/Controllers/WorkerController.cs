// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization.Policies;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Controllers;

/// <summary>
/// Controller for managing external worker connections in the compute separation model.
/// Provides APIs for the platform to link worker pods to this runtime.
/// </summary>
public sealed class WorkerController : Controller
{
    private readonly IWorkerConnectionManager _connectionManager;
    private readonly IScriptWebHostEnvironment _webHostEnvironment;
    private readonly ILogger _logger;

    public WorkerController(
        IWorkerConnectionManager connectionManager,
        IScriptWebHostEnvironment webHostEnvironment,
        ILoggerFactory loggerFactory)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _webHostEnvironment = webHostEnvironment ?? throw new ArgumentNullException(nameof(webHostEnvironment));
        _logger = loggerFactory.CreateLogger<WorkerController>();
    }

    /// <summary>
    /// Links an external worker to this runtime. Establishes an outbound gRPC
    /// connection to the worker proxy, performs the init handshake, and returns
    /// only after the worker is fully linked.
    /// </summary>
    [HttpPut]
    [Route("admin/workers/{workerId}")]
    [Authorize(Policy = PolicyNames.AdminAuthLevel)]
    public async Task<IActionResult> LinkWorker([FromRoute] string workerId, [FromBody] ExternalWorkerInfo request)
    {
        var requestStart = Stopwatch.GetTimestamp();
        _logger.LogInformation(
            "RuntimeLinkWorker request received. WorkerId: {workerId}, HasBody: {hasBody}, WorkerPodName: {workerPodName}, WorkerGrpcEndpoint: {workerGrpcEndpoint}, WorkerHttpEndpoint: {workerHttpEndpoint}.",
            workerId,
            request is not null,
            request?.WorkerPodName,
            request?.WorkerGrpcEndpoint,
            request?.WorkerHttpEndpoint);

        if (string.IsNullOrWhiteSpace(workerId))
        {
            _logger.LogWarning("RuntimeLinkWorker validation failed. Reason: MissingWorkerId, ElapsedMilliseconds: {elapsedMilliseconds}.", Stopwatch.GetElapsedTime(requestStart).TotalMilliseconds);
            return BadRequest("Worker id is required in the route.");
        }

        if (request is null)
        {
            _logger.LogWarning("RuntimeLinkWorker validation failed. WorkerId: {workerId}, Reason: MissingBody, ElapsedMilliseconds: {elapsedMilliseconds}.", workerId, Stopwatch.GetElapsedTime(requestStart).TotalMilliseconds);
            return BadRequest("Request body is required.");
        }

        if (_webHostEnvironment.InStandbyMode)
        {
            _logger.LogWarning("RuntimeLinkWorker validation failed. WorkerId: {workerId}, Reason: HostInStandbyMode, ElapsedMilliseconds: {elapsedMilliseconds}.", workerId, Stopwatch.GetElapsedTime(requestStart).TotalMilliseconds);
            return BadRequest("Cannot link workers before the host has been specialized.");
        }

        if (!string.IsNullOrWhiteSpace(request.WorkerPodName) &&
            !string.Equals(request.WorkerPodName, workerId, StringComparison.Ordinal))
        {
            _logger.LogWarning("RuntimeLinkWorker validation failed. WorkerId: {workerId}, WorkerPodName: {workerPodName}, Reason: WorkerIdMismatch, ElapsedMilliseconds: {elapsedMilliseconds}.", workerId, request.WorkerPodName, Stopwatch.GetElapsedTime(requestStart).TotalMilliseconds);
            return BadRequest($"Body '{nameof(request.WorkerPodName)}' '{request.WorkerPodName}' does not match route worker id '{workerId}'.");
        }

        if (string.IsNullOrWhiteSpace(request.WorkerGrpcEndpoint))
        {
            _logger.LogWarning("RuntimeLinkWorker validation failed. WorkerId: {workerId}, Reason: MissingGrpcEndpoint, ElapsedMilliseconds: {elapsedMilliseconds}.", workerId, Stopwatch.GetElapsedTime(requestStart).TotalMilliseconds);
            return BadRequest($"'{nameof(request.WorkerGrpcEndpoint)}' is required.");
        }

        if (!Uri.TryCreate(request.WorkerGrpcEndpoint, UriKind.Absolute, out Uri endpoint))
        {
            _logger.LogWarning("RuntimeLinkWorker validation failed. WorkerId: {workerId}, WorkerGrpcEndpoint: {workerGrpcEndpoint}, Reason: InvalidGrpcEndpoint, ElapsedMilliseconds: {elapsedMilliseconds}.", workerId, request.WorkerGrpcEndpoint, Stopwatch.GetElapsedTime(requestStart).TotalMilliseconds);
            return BadRequest($"'{request.WorkerGrpcEndpoint}' is not a valid URI.");
        }

        Uri workerHttpEndpoint = null;
        if (!string.IsNullOrWhiteSpace(request.WorkerHttpEndpoint) &&
            !Uri.TryCreate(request.WorkerHttpEndpoint, UriKind.Absolute, out workerHttpEndpoint))
        {
            _logger.LogWarning("RuntimeLinkWorker validation failed. WorkerId: {workerId}, WorkerHttpEndpoint: {workerHttpEndpoint}, Reason: InvalidHttpEndpoint, ElapsedMilliseconds: {elapsedMilliseconds}.", workerId, request.WorkerHttpEndpoint, Stopwatch.GetElapsedTime(requestStart).TotalMilliseconds);
            return BadRequest($"'{request.WorkerHttpEndpoint}' is not a valid URI.");
        }

        _logger.LogInformation("RuntimeLinkWorker request validated. WorkerId: {workerId}, WorkerGrpcEndpoint: {endpoint}, WorkerHttpEndpoint: {workerHttpEndpoint}, ElapsedMilliseconds: {elapsedMilliseconds}.", workerId, endpoint, workerHttpEndpoint, Stopwatch.GetElapsedTime(requestStart).TotalMilliseconds);

        // Check for duplicate before starting async work — return 409 Conflict.
        // Failed connections are auto-removed by WorkerConnectionService so the
        // platform can retry the same workerId without an explicit DELETE.
        if (_connectionManager.GetWorkerStatus(workerId) is not null)
        {
            _logger.LogWarning("RuntimeLinkWorker rejected duplicate worker. WorkerId: {workerId}, ElapsedMilliseconds: {elapsedMilliseconds}.", workerId, Stopwatch.GetElapsedTime(requestStart).TotalMilliseconds);
            return Conflict($"Worker '{workerId}' is already linked.");
        }

        try
        {
            var connectStart = Stopwatch.GetTimestamp();
            await _connectionManager.ConnectWorkerAsync(workerId, endpoint, workerHttpEndpoint, HttpContext.RequestAborted);
            _logger.LogInformation("RuntimeLinkWorker completed. WorkerId: {workerId}, ConnectElapsedMilliseconds: {connectElapsedMilliseconds}, TotalElapsedMilliseconds: {totalElapsedMilliseconds}.", workerId, Stopwatch.GetElapsedTime(connectStart).TotalMilliseconds, Stopwatch.GetElapsedTime(requestStart).TotalMilliseconds);
            return Ok();
        }
        catch (InvalidOperationException ex)
        {
            var message = $"Worker '{workerId}' link rejected.";
            _logger.LogWarning(ex, "{message} TotalElapsedMilliseconds: {totalElapsedMilliseconds}.", message, Stopwatch.GetElapsedTime(requestStart).TotalMilliseconds);
            return Conflict(message);
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            throw; // Let the framework handle client disconnection.
        }
        catch (Exception ex)
        {
            var message = $"Worker '{workerId}' connection failed.";
            _logger.LogError(ex, "{message} TotalElapsedMilliseconds: {totalElapsedMilliseconds}.", message, Stopwatch.GetElapsedTime(requestStart).TotalMilliseconds);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, message);
        }
    }
}
