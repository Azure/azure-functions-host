// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Script.AppCapabilities;
using Microsoft.Azure.WebJobs.Script.WebHost.AppCapabilities;
using Microsoft.Azure.WebJobs.Script.WebHost.Filters;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization.Policies;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

#nullable enable

namespace Microsoft.Azure.WebJobs.Script.WebHost.Controllers
{
    public sealed class AppCapabilitiesController : Controller
    {
        // Use the same limit as MaxTriggersStringLength for consistency with similar admin APIs
        private const int MaxResponseSizeBytes = ScriptConstants.MaxTriggersStringLength;
        private readonly IOptionsMonitor<AppCapabilitiesOptions> _capabilitiesOptions;
        private readonly ILogger<AppCapabilitiesController> _logger;

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
            IDictionary<string, string> capabilities = _capabilitiesOptions.CurrentValue;
            var validationResult = ValidateResponseSize(capabilities);

            if (!validationResult.IsValid)
            {
                return StatusCode(StatusCodes.Status413PayloadTooLarge, new { error = validationResult.ErrorMessage });
            }

            return Content(validationResult.SerializedResponse!, "application/json");
        }

        [HttpGet]
        [Route("admin/capabilities/{name}")]
        [Authorize(Policy = PolicyNames.AdminAuthLevel)]
        [RequiresRunningHost]
        [ResourceContainsSecrets]
        public IActionResult Get(string name)
        {
            IDictionary<string, string> capabilities = _capabilitiesOptions.CurrentValue;

            if (capabilities.TryGetValue(name, out var value))
            {
                var validationResult = ValidateResponseSize(value);

                if (!validationResult.IsValid)
                {
                    return StatusCode(StatusCodes.Status413PayloadTooLarge, new { error = validationResult.ErrorMessage });
                }

                return Content(validationResult.SerializedResponse!, "application/json");
            }

            return NotFound();
        }

        private (bool IsValid, string? ErrorMessage, string? SerializedResponse) ValidateResponseSize(IDictionary<string, string> capabilities)
        {
            var serializedResponse = JsonSerializer.Serialize(capabilities, DictionaryJsonContext.Default.IDictionaryStringString);
            return ValidateSerializedResponseSize(serializedResponse, "Capabilities response");
        }

        private (bool IsValid, string? ErrorMessage, string? SerializedResponse) ValidateResponseSize(string value)
        {
            var serializedValue = JsonSerializer.Serialize(value);
            return ValidateSerializedResponseSize(serializedValue, "Capability value");
        }

        private (bool IsValid, string? ErrorMessage, string? SerializedResponse) ValidateSerializedResponseSize(string serializedResponse, string responseType)
        {
            var responseSize = System.Text.Encoding.UTF8.GetByteCount(serializedResponse);

            if (responseSize <= MaxResponseSizeBytes)
            {
                return (true, null, serializedResponse);
            }

            var errorMessage = $"{responseType} size ({responseSize} bytes) exceeds maximum allowed size ({MaxResponseSizeBytes} bytes).";
            _logger.LogError("{ResponseType} size ({ResponseSize} bytes) exceeds maximum allowed size ({MaxResponseSizeBytes} bytes).", responseType, responseSize, MaxResponseSizeBytes);

            return (false, errorMessage, null);
        }
    }
}
