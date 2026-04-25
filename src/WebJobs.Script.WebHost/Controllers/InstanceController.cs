// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
    /// health checks, and runtime state reporting.
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

                // Determine which of the three assignment modes is being used:
                //   1. encryptedContext (legacy encrypted HostAssignmentContext)
                //   2. assignmentContext (legacy plaintext HostAssignmentContext)
                //   3. apiServerAssignmentRequest + environment (Goal 3 runtime container)
                bool hasEncrypted = !string.IsNullOrEmpty(hostAssignmentRequest.EncryptedContext);
                bool hasLegacyContext = hostAssignmentRequest.AssignmentContext is not null;
                bool hasRuntimePayload = hostAssignmentRequest.ApiServerAssignmentRequest is not null;
                int modeCount = (hasEncrypted ? 1 : 0) + (hasLegacyContext ? 1 : 0) + (hasRuntimePayload ? 1 : 0);

                if (modeCount == 0)
                {
                    return BadRequest("At least one of 'assignmentContext', 'encryptedContext', or 'apiServerAssignmentRequest' must be provided.");
                }

                if (modeCount > 1)
                {
                    return BadRequest("Only one of 'assignmentContext', 'encryptedContext', or 'apiServerAssignmentRequest' may be set.");
                }

                if (hasRuntimePayload)
                {
                    // Goal 3 runtime container assignment — normalize to HostAssignmentContext
                    // so the rest of the pipeline (SetContext → ValidateContext → StartAssignment) is unchanged.
                    if (hostAssignmentRequest.Environment is null)
                    {
                        return BadRequest("'environment' is required when 'apiServerAssignmentRequest' is present.");
                    }

                    if (!User.HasClaim(SecurityConstants.AssignUnencryptedClaimType, "true"))
                    {
                        _logger.LogWarning("Required claims missing for invoking runtime container assignment");
                        return Forbid();
                    }

                    var apiReq = hostAssignmentRequest.ApiServerAssignmentRequest;
                    var lastModifiedEpoch = Math.Max(apiReq.ConfigLastModifiedTime, apiReq.ContentLastModifiedTime);
                    var lastModifiedTime = lastModifiedEpoch > 0
                        ? DateTimeOffset.FromUnixTimeSeconds(lastModifiedEpoch).UtcDateTime
                        : DateTime.UtcNow;

                    hostAssignmentRequest.AssignmentContext = new HostAssignmentContext
                    {
                        SiteId = int.TryParse(apiReq.SiteId, out var parsedSiteId) ? parsedSiteId : 0,
                        SiteName = apiReq.AppName,
                        Environment = hostAssignmentRequest.Environment,
                        LastModifiedTime = lastModifiedTime,
                        SiteUpdateId = lastModifiedEpoch,
                        IsWarmupRequest = false,
                    };

                    // Clear the runtime-specific fields so downstream code only sees AssignmentContext.
                    hostAssignmentRequest.ApiServerAssignmentRequest = null;
                    hostAssignmentRequest.Environment = null;

                    _logger.LogDebug("Starting runtime container assignment for app '{AppName}' (siteId={SiteId}, instanceMemory={InstanceMemory}MB).",
                        apiReq.AppName, apiReq.SiteId, apiReq.InstanceMemory);
                }
                else if (hasEncrypted)
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
        /// Returns a snapshot of this runtime instance's state: linked worker
        /// count and request-slot accounting. This is the same payload that is
        /// published to the mesh service as <c>publish-runtime-state</c>.
        /// </summary>
        /// <remarks>
        /// Returns <c>503 Service Unavailable</c> when compute separation is not
        /// enabled (no <see cref="IRuntimeStateManager"/> is registered).
        /// </remarks>
        /// <example>
        /// <code>
        /// GET /admin/instance/state
        /// </code>
        /// </example>
        [HttpGet]
        [Route("admin/instance/state")]
        [Authorize(Policy = PolicyNames.AdminAuthLevel)]
        public IActionResult GetState()
        {
            var runtimeStateManager = HttpContext.RequestServices.GetService<IRuntimeStateManager>();
            if (runtimeStateManager is null)
            {
                _logger.LogWarning("GetState called but the external workers feature is not enabled.");
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            return Ok(runtimeStateManager.GetState());
        }
    }
}
