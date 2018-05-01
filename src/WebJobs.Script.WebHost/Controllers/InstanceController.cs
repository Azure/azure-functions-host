// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
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
        private readonly WebScriptHostManager _scriptHostManager;
        private readonly ScriptSettingsManager _settingsManager;
        private readonly IInstanceManager _instanceManager;

        public InstanceController(WebScriptHostManager scriptHostManager, ScriptSettingsManager settingsManager, IInstanceManager instanceManager)
        {
            _scriptHostManager = scriptHostManager;
            _settingsManager = settingsManager;
            _instanceManager = instanceManager;
        }

        [HttpPost]
        [Route("admin/instance/assign")]
        [Authorize(Policy = PolicyNames.AdminAuthLevel)]
        public IActionResult Assign([FromBody] EncryptedHostAssignmentContext encryptedAssignmentContext)
        {
            var containerKey = _settingsManager.GetSetting(EnvironmentSettingNames.ContainerEncryptionKey);
            var assignmentContext = encryptedAssignmentContext.Decrypt(containerKey);
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
    }
}
