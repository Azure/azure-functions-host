// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authentication;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization.Policies;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Controllers
{
    /// <summary>
    /// Controller responsible for instance operations such as specialization,
    /// lifecycle management, and health checks.
    /// </summary>
    public class InstanceController : Controller
    {
        private readonly IEnvironment _environment;
        private readonly IInstanceManager _instanceManager;
        private readonly IScriptHostManager _scriptHostManager;
        private readonly IMetricsLogger _metricsLogger;
        private readonly ILogger _logger;
        private readonly StartupContextProvider _startupContextProvider;

        public InstanceController(IEnvironment environment, IInstanceManager instanceManager, IScriptHostManager scriptHostManager, ILoggerFactory loggerFactory, StartupContextProvider startupContextProvider, IMetricsLogger metricsLogger)
        {
            _environment = environment;
            _instanceManager = instanceManager;
            _scriptHostManager = scriptHostManager;
            _logger = loggerFactory.CreateLogger<InstanceController>();
            _startupContextProvider = startupContextProvider;
            _metricsLogger = metricsLogger;
        }

        [HttpPost]
        [Route("admin/instance/assign")]
        [Authorize(Policy = PolicyNames.AdminAuthLevel)]
        public async Task<IActionResult> Assign([FromBody] HostAssignmentRequest hostAssignmentRequest)
        {
            using (_metricsLogger.LatencyEvent(MetricEventNames.LinuxContainerSpecializationAssign))
            {
                if (hostAssignmentRequest == null)
                {
                    return BadRequest($"{nameof(hostAssignmentRequest)} cannot be null.");
                }

                if (string.IsNullOrEmpty(hostAssignmentRequest.EncryptedContext) &&
                    hostAssignmentRequest.AssignmentContext is null)
                {
                    return BadRequest($"At least one of {nameof(HostAssignmentRequest.AssignmentContext)} or {nameof(HostAssignmentRequest.EncryptedContext)} must be provided.");
                }

                if (!string.IsNullOrEmpty(hostAssignmentRequest.EncryptedContext) &&
                    hostAssignmentRequest.AssignmentContext is not null)
                {
                    return BadRequest($"Only one of {nameof(HostAssignmentRequest.AssignmentContext)} or {nameof(HostAssignmentRequest.EncryptedContext)} may be set.");
                }

                if (!string.IsNullOrEmpty(hostAssignmentRequest.EncryptedContext))
                {
                    _logger.LogDebug("Starting container assignment. ContextLength is {ContextLength}", hostAssignmentRequest.EncryptedContext.Length);
                }
                else
                {
                    if (!User.HasClaim(SecurityConstants.AssignUnencryptedClaimType, "true"))
                    {
                        _logger.LogWarning("Required claims missing for invoking unencrypted assignment");
                        return Forbid();
                    }
                    _logger.LogDebug("Starting container assignment.");
                }

                var assignmentContext = _startupContextProvider.SetContext(hostAssignmentRequest);

                // before starting the assignment we want to perform as much
                // up front validation on the context as possible
                string error = await _instanceManager.ValidateContext(assignmentContext);
                if (error != null)
                {
                    return StatusCode(StatusCodes.Status400BadRequest, error);
                }

                // Wait for Sidecar specialization to complete before returning ok.
                // This shouldn't take too long so ok to do this sequentially.
                error = await _instanceManager.SpecializeMSISidecar(assignmentContext);
                if (error != null)
                {
                    return StatusCode(StatusCodes.Status500InternalServerError, error);
                }

                var succeeded = _instanceManager.StartAssignment(assignmentContext);

                return succeeded
                    ? Accepted()
                    : StatusCode(StatusCodes.Status409Conflict, "Instance already assigned");
            }
        }

        [HttpGet]
        [Route("admin/instance/info")]
        [Authorize(Policy = PolicyNames.AdminAuthLevel)]
        public IActionResult GetInstanceInfo()
        {
            return Ok(_instanceManager.GetInstanceInfo());
        }

        [HttpGet]
        [Route("admin/instance/http-health")]
        public IActionResult GetHttpHealthStatus()
        {
            // Reaching here implies that http health of the container is ok.
            return Ok();
        }

        /// <summary>
        /// Stops the runtime pod. Enables drain mode to stop trigger listeners
        /// and stop accepting new invocations, then drains and disconnects all
        /// connected external workers in parallel.
        /// Called by the Go Proxy when the platform decides to stop the entire runtime pod.
        /// </summary>
        /// <example>
        /// <code>
        /// POST /admin/instance/stop
        /// </code>
        /// </example>
        [HttpPost]
        [Route("admin/instance/stop")]
        [Authorize(Policy = PolicyNames.AdminAuthLevel)]
        public IActionResult Stop()
        {
            _logger.LogInformation("Received request to stop the runtime instance.");

            if (!Utility.TryGetHostService(_scriptHostManager, out IDrainModeManager drainModeManager))
            {
                _logger.LogWarning("Stop requested but ScriptHost is not ready (IDrainModeManager unavailable).");
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            // IWorkerConnectionManager is only registered when external workers are enabled.
            // In non-compute-separation scenarios, /stop still drains the host but has no workers to disconnect.
            var connectionManager = HttpContext.RequestServices.GetService<IWorkerConnectionManager>();

            // Fire-and-forget: drain host listeners, then disconnect all workers.
            _ = StopCoreAsync(drainModeManager, connectionManager)
                .ContinueWith(
                    t =>
                    {
                        if (t.IsFaulted)
                        {
                            _logger.LogError(t.Exception, "Error during runtime stop.");
                        }
                        else
                        {
                            _logger.LogInformation("Runtime stop completed.");
                        }
                    },
                    TaskScheduler.Default);

            return Accepted();
        }

        private async Task StopCoreAsync(IDrainModeManager drainModeManager, IWorkerConnectionManager connectionManager)
        {
            // Step 1: Stop trigger listeners and stop accepting new invocations.
            _logger.LogInformation("Enabling drain mode.");
            await drainModeManager.EnableDrainModeAsync(CancellationToken.None);

            // Step 2: Drain in-flight invocations and disconnect all workers (if any).
            if (connectionManager is not null)
            {
                _logger.LogInformation("Draining and disconnecting all workers.");
                await connectionManager.DrainAndDisconnectAllAsync(CancellationToken.None);
            }
        }
    }
}
