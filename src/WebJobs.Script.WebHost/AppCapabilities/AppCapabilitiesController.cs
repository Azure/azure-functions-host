// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Script.AppCapabilities;
using Microsoft.Azure.WebJobs.Script.WebHost.Filters;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization.Policies;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

#nullable enable

namespace Microsoft.Azure.WebJobs.Script.WebHost.Controllers
{
    public sealed class AppCapabilitiesController : Controller
    {
        private readonly IOptionsMonitor<AppCapabilitiesOptions> _capabilitiesOptions;
        private readonly ILogger<AppCapabilitiesController> _logger;
        private static readonly ErrorResponse _capabilitiesValidationErrorResponse = new ErrorResponse(
            "InvalidAppCapabilities",
            "The application capabilities configuration is invalid. Please check the logs for more details.");

        public AppCapabilitiesController(IOptionsMonitor<AppCapabilitiesOptions> capabilitiesOptions,
            ILogger<AppCapabilitiesController> logger)
        {
            _capabilitiesOptions = capabilitiesOptions;
            _logger = logger;
        }

        [HttpGet]
        [Route("admin/capabilities")]
        [Authorize(Policy = PolicyNames.AdminAuthLevel)]
        [RequiresRunningHost]
        [ResourceContainsSecrets]
        public IActionResult GetCapabilities()
        {
            try
            {
                IDictionary<string, string> capabilities = _capabilitiesOptions.CurrentValue;
                return Ok(capabilities);
            }
            catch (OptionsValidationException ex)
            {
                _logger.LogError(ex, "Capabilities validation failed.");
                return StatusCode(500, _capabilitiesValidationErrorResponse);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unexpected error occurred while retrieving capabilities.");
                return StatusCode(500, ErrorResponse.InternalServerError("An unexpected error occurred while retrieving capabilities. Please check the logs for more details."));
            }
        }

        [HttpGet]
        [Route("admin/capabilities/{name}")]
        [Authorize(Policy = PolicyNames.AdminAuthLevel)]
        [RequiresRunningHost]
        [ResourceContainsSecrets]
        public IActionResult Get(string name)
        {
            try
            {
                IDictionary<string, string> capabilities = _capabilitiesOptions.CurrentValue;

                if (capabilities.TryGetValue(name, out string? value))
                {
                    return Ok(value);
                }
                else
                {
                    var errorResponse = ErrorResponse.NotFound($"The capability '{name}' was not found.");
                    return NotFound(errorResponse);
                }
            }
            catch (OptionsValidationException ex)
            {
                _logger.LogError(ex, "Capabilities validation failed.");
                return StatusCode(500, _capabilitiesValidationErrorResponse);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unexpected error occurred while retrieving capability '{CapabilityName}'.", name);
                return StatusCode(500, ErrorResponse.InternalServerError("An unexpected error occurred while retrieving capabilities. Please check the logs for more details."));
            }
        }
    }
}
