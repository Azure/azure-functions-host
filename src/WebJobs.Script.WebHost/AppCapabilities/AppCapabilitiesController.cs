// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Script.AppCapabilities;
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

        public AppCapabilitiesController(IOptionsMonitor<AppCapabilitiesOptions> capabilitiesOptions, ILogger<AppCapabilitiesController> logger)
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

            return Ok(capabilities);
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
                var trimmedValue = ValidateAndTrimValue(name, value);
                return Ok(trimmedValue);
            }

            return NotFound();
        }

        private (bool IsValid, string? ErrorMessage) ValidateResponseSize(IDictionary<string, string> capabilities)
        {
            var serializedResponse = JsonSerializer.Serialize(capabilities);
            var responseSize = System.Text.Encoding.UTF8.GetByteCount(serializedResponse);

            if (responseSize <= MaxResponseSizeBytes)
            {
                return (true, null);
            }

            var errorMessage = $"Capabilities response size ({responseSize} bytes) exceeds maximum allowed size ({MaxResponseSizeBytes} bytes).";
            _logger.LogError(errorMessage);

            return (false, errorMessage);
        }

        private string ValidateAndTrimValue(string name, string value)
        {
            if (value is null)
            {
                return value;
            }

            var valueSize = System.Text.Encoding.UTF8.GetByteCount(value);

            if (valueSize <= MaxResponseSizeBytes)
            {
                return value;
            }

            _logger.LogWarning("Capability '{CapabilityName}' value size ({ValueSize} bytes) exceeds maximum allowed size ({MaxSize} bytes). Trimming value.", name, valueSize, MaxResponseSizeBytes);

            // Trim the value to fit within the max size
            var maxChars = MaxResponseSizeBytes;
            var trimmedValue = value.Length > maxChars ? value[..maxChars] : value;

            // Ensure the trimmed value doesn't exceed the byte limit
            while (System.Text.Encoding.UTF8.GetByteCount(trimmedValue) > MaxResponseSizeBytes)
            {
                trimmedValue = trimmedValue[..^1];
            }

            return trimmedValue;
        }
    }
}