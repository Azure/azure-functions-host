// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Middleware
{
    /// <summary>
    /// This middleware simulate the specialization event.
    /// </summary>
    public sealed class SpecializationSimulatorMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IEnvironment _environment;
        private readonly ILogger<SpecializationSimulatorMiddleware> _logger;
        private int _specializationDone = 0;

        public SpecializationSimulatorMiddleware(RequestDelegate next, IEnvironment environment, ILogger<SpecializationSimulatorMiddleware> logger)
        {
            _next = next;
            _environment = environment;
            _logger = logger;
        }

        /// <summary>
        /// Processes the incoming request. When the <c>forcespecialization</c> query parameter is present,
        /// simulates the specialization event by applying environment variables from an optional JSON
        /// request body and then triggering specialization.
        /// </summary>
        /// <param name="context">The current <see cref="HttpContext"/>.</param>
        /// <example>
        /// Send a POST with <c>?forcespecialization</c> and a JSON body to set environment variables:
        /// <code>
        /// POST /?forcespecialization
        /// Content-Type: application/json
        ///
        /// {
        ///   "AzureWebJobsScriptRoot": "C:\\app",
        ///   "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated"
        /// }
        /// </code>
        /// </example>
        public async Task InvokeAsync(HttpContext context)
        {
            var forceSpecialization = context.Request.Query.ContainsKey("forcespecialization");
            if (forceSpecialization)
            {
                _logger.LogInformation("Attempting to simulate specialization event.");

                if (Interlocked.Exchange(ref _specializationDone, 1) == 0)
                {
                    await ApplyEnvironmentVariablesFromBodyAsync(context.Request);

                    var scriptRoot = _environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsScriptRoot);
                    if (string.IsNullOrWhiteSpace(scriptRoot))
                    {
                        const string error = $"Invalid script root. Provide the function function app payload location via {EnvironmentSettingNames.AzureWebJobsScriptRoot} environment variable";
                        throw new InvalidOperationException(error);
                    }

                    _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteUsePlaceholderDotNetIsolated, "1");
                    _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");
                    _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteContainerReady, "1");
                }
                else
                {
                    _logger.LogWarning("Specialization simulation event was already invoked once. No actions taken this time.");
                }
            }

            await _next(context);
        }

        private async Task ApplyEnvironmentVariablesFromBodyAsync(HttpRequest request)
        {
            if (request.ContentLength is null or 0)
            {
                return;
            }

            Dictionary<string, string> environmentVariables;
            try
            {
                environmentVariables = await request.ReadFromJsonAsync<Dictionary<string, string>>();
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize environment variables from request body. Skipping payload processing.");
                return;
            }

            if (environmentVariables is null or { Count: 0 })
            {
                return;
            }

            foreach (KeyValuePair<string, string> entry in environmentVariables)
            {
                _logger.LogInformation("Setting environment variable '{Name}' from specialization payload.", entry.Key);
                _environment.SetEnvironmentVariable(entry.Key, entry.Value);
            }
        }
    }
}
