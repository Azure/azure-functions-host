// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Script.AppCapabilities;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization.Policies;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Controllers
{
    public class AppCapabilitiesController
    {
        private readonly AppCapabilitiesOptions _capabilitiesOptions;

        public AppCapabilitiesController(IOptions<AppCapabilitiesOptions> capabilitiesOptions)
        {
            _capabilitiesOptions = capabilitiesOptions.Value;
        }

        [HttpGet]
        [Route("admin/capabilities")]
        [Authorize(Policy = PolicyNames.AdminAuthLevel)]
        public IActionResult GetCapabilities()
        {
            return new OkObjectResult(_capabilitiesOptions.Capabilities);
        }

        [HttpGet]
        [Route("admin/capabilities/{name}")]
        [Authorize(Policy = PolicyNames.AdminAuthLevel)]
        public IActionResult Get(string name)
        {
            var capabilities = _capabilitiesOptions.Capabilities;

            var capability = capabilities.FirstOrDefault(kvp => string.Equals(kvp.Key, name, System.StringComparison.OrdinalIgnoreCase));

            if (capability.Key is null)
            {
                return new NotFoundResult();
            }

            return new OkObjectResult(capability.Value);
        }
    }
}
