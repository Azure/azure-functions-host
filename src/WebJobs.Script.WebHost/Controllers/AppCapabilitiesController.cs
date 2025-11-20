// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Script.WebHost.AppCapabilities;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization.Policies;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Controllers
{
    public class AppCapabilitiesController
    {
        private readonly IOptions<AppCapabilitiesOptions> _capabilitiesOptions;

        public AppCapabilitiesController(IOptions<AppCapabilitiesOptions> capabilitiesOptions)
        {
            _capabilitiesOptions = capabilitiesOptions;
        }

        [HttpGet]
        [Route("admin/capabilities")]
        [Authorize(Policy = PolicyNames.AdminAuthLevel)]
        public IActionResult GetCapabilities()
        {
            var capabilities = _capabilitiesOptions.Value.Capabilities;

            var response = new
            {
                capabilities = capabilities.Select(kvp => new
                {
                    name = kvp.Key,
                    source = kvp.Value.Source,
                    version = kvp.Value.Version,
                    metadata = kvp.Value.Metadata
                })
            };

            return new OkObjectResult(response);
        }

        [HttpGet]
        [Route("admin/capabilities/{name}")]
        [Authorize(Policy = PolicyNames.AdminAuthLevel)]
        public IActionResult Get(string name)
        {
            var capabilities = _capabilitiesOptions.Value.Capabilities;

            var capability = capabilities.FirstOrDefault(kvp => string.Equals(kvp.Key, name, System.StringComparison.OrdinalIgnoreCase));

            if (capability.Key is null)
            {
                return new NotFoundResult();
            }

            var response = new
            {
                name = capability.Key,
                source = capability.Value.Source,
                version = capability.Value.Version,
                metadata = capability.Value.Metadata
            };

            return new OkObjectResult(response);
        }
    }
}
