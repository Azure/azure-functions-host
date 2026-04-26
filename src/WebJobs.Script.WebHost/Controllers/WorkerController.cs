// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
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
        if (string.IsNullOrWhiteSpace(workerId))
        {
            return BadRequest("Worker id is required in the route.");
        }

        if (request is null)
        {
            return BadRequest("Request body is required.");
        }

        if (_webHostEnvironment.InStandbyMode)
        {
            return BadRequest("Cannot link workers before the host has been specialized.");
        }

        if (!string.IsNullOrWhiteSpace(request.WorkerPodName) &&
            !string.Equals(request.WorkerPodName, workerId, StringComparison.Ordinal))
        {
            return BadRequest($"Body '{nameof(request.WorkerPodName)}' '{request.WorkerPodName}' does not match route worker id '{workerId}'.");
        }

        if (string.IsNullOrWhiteSpace(request.WorkerGrpcEndpoint))
        {
            return BadRequest($"'{nameof(request.WorkerGrpcEndpoint)}' is required.");
        }

        if (!Uri.TryCreate(request.WorkerGrpcEndpoint, UriKind.Absolute, out Uri endpoint))
        {
            return BadRequest($"'{request.WorkerGrpcEndpoint}' is not a valid URI.");
        }

        _logger.LogInformation("Received worker link request for '{workerId}' at {endpoint}.", workerId, endpoint);

        // Check for duplicate before starting async work — return 409 Conflict.
        // Failed connections are auto-removed by WorkerConnectionService so the
        // platform can retry the same workerId without an explicit DELETE.
        if (_connectionManager.GetWorkerStatus(workerId) is not null)
        {
            return Conflict($"Worker '{workerId}' is already linked.");
        }

        try
        {
            await _connectionManager.ConnectWorkerAsync(workerId, endpoint, HttpContext.RequestAborted);
            return Ok();
        }
        catch (InvalidOperationException ex)
        {
            var message = $"Worker '{workerId}' link rejected.";
            _logger.LogWarning(ex, message);
            return Conflict(message);
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            throw; // Let the framework handle client disconnection.
        }
        catch (Exception ex)
        {
            var message = $"Worker '{workerId}' connection failed.";
            _logger.LogError(ex, message);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, message);
        }
    }
}
