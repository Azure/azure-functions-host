// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Script.AppCapabilities;
using Microsoft.Azure.WebJobs.Script.WebHost.Filters;
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
        [RequiresRunningHost]
        public IActionResult GetCapabilities([FromServices] IAppCapabilitiesProvider appCapabilitiesProvider)
        {
            // Get capabilities from options
            var optionsCapabilities = _capabilitiesOptions.Capabilities ?? new Dictionary<string, string>();

            // Get capabilities from provider (worker)
            var providerCapabilities = appCapabilitiesProvider?.GetCapabilities() ?? new Dictionary<string, string>();

            // Merge: worker/provider wins on collision
            var merged = optionsCapabilities
                .Concat(providerCapabilities)
                .GroupBy(kvp => kvp.Key, System.StringComparer.OrdinalIgnoreCase)
                .Select(g => g.Last()) // provider comes after options, so Last() wins
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, System.StringComparer.OrdinalIgnoreCase);

            return new OkObjectResult(merged);
        }

        [HttpGet]
        [Route("admin/capabilities/{name}")]
        [Authorize(Policy = PolicyNames.AdminAuthLevel)]
        [RequiresRunningHost]
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
