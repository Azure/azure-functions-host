// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authentication;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization.Policies;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Controllers
{
    /// <summary>
    /// Controller responsible for instance operations that are orthogonal to the script host.
    /// An instance is an unassigned generic container running with the runtime in standby mode.
    /// These APIs are used by the AppService Controller to validate standby instance status and info.
    /// </summary>
    public class InstanceController : Controller
    {
        private readonly IEnvironment _environment;
        private readonly IInstanceManager _instanceManager;
        private readonly ILogger _logger;
        private readonly StartupContextProvider _startupContextProvider;

        public InstanceController(IEnvironment environment, IInstanceManager instanceManager, ILoggerFactory loggerFactory, StartupContextProvider startupContextProvider)
        {
            _environment = environment;
            _instanceManager = instanceManager;
            _logger = loggerFactory.CreateLogger<InstanceController>();
            _startupContextProvider = startupContextProvider;
        }

        [HttpPost]
        [Route("admin/instance/assign")]
        [Authorize(Policy = PolicyNames.AdminAuthLevel)]
        public async Task<IActionResult> Assign([FromBody] HostAssignmentRequest hostAssignmentContext)
        {
            if (string.IsNullOrEmpty(hostAssignmentContext.EncryptedContext) &&
                hostAssignmentContext.AssignmentContext is null)
            {
                return BadRequest("At least one of 'assignmentContext' or 'encryptedContext' must be provided.");
            }
            if (!string.IsNullOrEmpty(hostAssignmentContext.EncryptedContext) &&
                hostAssignmentContext.AssignmentContext is not null)
            {
                return BadRequest("Only one of 'assignmentContext' or 'encryptedContext' may be set.");
            }
            if (!string.IsNullOrEmpty(hostAssignmentContext.EncryptedContext))
            {
                _logger.LogDebug("Starting container assignment. ContextLength is {ContextLength}", hostAssignmentContext.EncryptedContext.Length);
            }
            else
            {
                if (User != null &&
                    !User.HasClaim(c => c.Type == SecurityConstants.AssignUnencryptedClaimType && c.Value == "true"))
                {
                    _logger.LogWarning("Required claims missing for invoking unencrypted assignment");
                    return StatusCode(StatusCodes.Status403Forbidden, new { status = "Required claims missing for invoking unencrypted assignment" });
                }
                _logger.LogDebug("Starting container assignment.");
            }

            var assignmentContext = _startupContextProvider.SetContext(hostAssignmentContext);

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
    }
}
