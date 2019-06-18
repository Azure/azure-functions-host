// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Azure.WebJobs.Script.WebHost.Security;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization.Policies;

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

        public InstanceController(IEnvironment environment, IInstanceManager instanceManager)
        {
            _environment = environment;
            _instanceManager = instanceManager;
        }

        [HttpPost]
        [Route("admin/instance/assign")]
        [Authorize(Policy = PolicyNames.AdminAuthLevel)]
        public async Task<IActionResult> Assign([FromBody] EncryptedHostAssignmentContext encryptedAssignmentContext)
        {
            var containerKey = _environment.GetEnvironmentVariable(EnvironmentSettingNames.ContainerEncryptionKey);
            var assignmentContext = encryptedAssignmentContext.Decrypt(containerKey);

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

            var result = _instanceManager.StartAssignment(assignmentContext);

            return result
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

        /// <summary>
        /// This endpoint generates a temporary x-ms-site-restricted-token for core tool
        /// to access KuduLite zipdeploy endpoint in Linux Consumption
        /// </summary>
        /// <returns>
        /// 200 on token generated
        /// 400 on non-Linux container environment
        /// 404 on WEBSITE_AUTH_ENCRYPTION_KEY is not set
        /// </returns>
        [HttpGet]
        [Route("admin/getsitetoken")]
        [Authorize(Policy = PolicyNames.AdminAuthLevel)]
        public IActionResult GetSiteToken()
        {
            if (!_environment.IsLinuxContainerEnvironment())
            {
                return BadRequest("Endpoint is only available when running in Linux Container");
            }

            string websiteEncryptionKey = _environment.GetEnvironmentVariable(EnvironmentSettingNames.WebSiteAuthEncryptionKey);
            if (string.IsNullOrEmpty(websiteEncryptionKey))
            {
                return NotFound("WEBSITE_AUTH_ENCRYPTION_KEY is not set.");
            }

            string requestHeaderToken = SimpleWebTokenHelper.CreateToken(DateTime.UtcNow.AddMinutes(30), websiteEncryptionKey.ToKeyBytes());
            return Ok(requestHeaderToken);
        }
    }
}
