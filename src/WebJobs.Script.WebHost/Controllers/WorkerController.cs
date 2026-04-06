// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using Microsoft.AspNetCore.Authorization;
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
public class WorkerController : Controller
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
    /// Links a worker pod to this runtime. Initiates an outbound gRPC connection
    /// to the worker proxy and returns immediately. The connection and handshake
    /// complete in the background.
    /// </summary>
    [HttpPost]
    [Route("admin/workers/link")]
    [Authorize(Policy = PolicyNames.AdminAuthLevel)]
    public IActionResult Link([FromBody] WorkerLinkRequest request)
    {
        if (_webHostEnvironment.InStandbyMode)
        {
            return BadRequest("Cannot link workers before the host has been specialized.");
        }

        if (request is null)
        {
            return BadRequest("Request body is required.");
        }

        if (string.IsNullOrWhiteSpace(request.GrpcEndpoint))
        {
            return BadRequest($"'{nameof(request.GrpcEndpoint)}' is required.");
        }

        if (!Uri.TryCreate(request.GrpcEndpoint, UriKind.Absolute, out Uri endpoint))
        {
            return BadRequest($"'{request.GrpcEndpoint}' is not a valid URI.");
        }

        string workerId = request.WorkerId;
        if (string.IsNullOrWhiteSpace(workerId))
        {
            workerId = $"w_{Guid.NewGuid():N}"[..10];
        }

        _logger.LogInformation("Received worker link request for '{workerId}' at {endpoint}.", workerId, endpoint);

        // Build the response before starting async work. ConnectWorkerAsync sets
        // _workerStates on a background thread, so reading it immediately after
        // fire-and-forget would race and likely return null.
        var info = new WorkerConnectionInfo
        {
            WorkerId = workerId,
            State = WorkerConnectionState.Connecting
        };

        // Fire-and-forget: kick off the connection in the background,
        // matching the pattern used by HostController.Drain and InstanceController.Assign.
        _ = _connectionManager.ConnectWorkerAsync(workerId, endpoint, CancellationToken.None)
            .ContinueWith(
                t =>
                {
                    if (t.IsFaulted)
                    {
                        _logger.LogError(t.Exception, "Worker '{workerId}' connection failed.", workerId);
                    }
                });

        return Accepted(info);
    }
}
