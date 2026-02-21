// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Script.AppCapabilities;
using Microsoft.Azure.WebJobs.Script.WebHost.Filters;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization.Policies;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Controllers
{
    public class AppCapabilitiesController : Controller
    {
        private readonly IOptionsMonitor<AppCapabilitiesOptions> _capabilitiesOptions;
        private readonly ILogger<AppCapabilitiesController> _logger;

        public AppCapabilitiesController(IOptionsMonitor<AppCapabilitiesOptions> capabilitiesOptions, ILogger<AppCapabilitiesController> logger)
        {
            _capabilitiesOptions = capabilitiesOptions;
            _logger = logger;
        }

        [HttpGet]
        [Route("admin/capabilities")]
        [Authorize(Policy = PolicyNames.AdminAuthLevel)]
        [RequiresRunningHost]
        public IActionResult GetCapabilities()
        {
            try
            {
                var capabilities = _capabilitiesOptions.CurrentValue.Capabilities;
                return new OkObjectResult(capabilities);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while retrieving capabilities.");
                return StatusCode(500, "An error occurred while retrieving capabilities.");
            }
        }

        [HttpGet]
        [Route("admin/capabilities/{name}")]
        [Authorize(Policy = PolicyNames.AdminAuthLevel)]
        [RequiresRunningHost]
        public IActionResult Get(string name)
        {
            var capabilities = _capabilitiesOptions.CurrentValue.Capabilities;

            if (capabilities.TryGetValue(name, out var value))
            {
                return new OkObjectResult(value);
            }

            return new NotFoundResult();
        }
    }
}
