// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

#nullable enable

namespace Microsoft.Azure.WebJobs.Script.AppCapabilities
{
    internal sealed class AppCapabilitiesOptionsSetup(IConfiguration configuration) : IConfigureOptions<AppCapabilitiesOptions>
    {
        private readonly IConfiguration _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        private readonly string configSectionName = "azureFunctionsJobHost:appCapabilities";

        /// <summary>
        /// Configures the <see cref="AppCapabilitiesOptions"/> by reading from all available configuration sources.
        /// </summary>
        /// <param name="options">The options instance to configure.</param>
        public void Configure(AppCapabilitiesOptions options)
        {
            // Read from host.json under appCapabilities
            /* Example:
             * {
                   "version": "2.0",
                  "logging": {
                    "applicationInsights": {
                      "samplingSettings": {
                        "isEnabled": true,
                        "excludedTypes": "Request"
                      },
                      "enableLiveMetricsFilters": true
                    }
                  },
                  "appCapabilities":
                    {
                    "mcp": {
                      "endpoint": "https://mcp.microsoft.com"
                    }
                  }
                }
            */
            var jobHostCapabilitiesSection = _configuration.GetSection(configSectionName);
            if (jobHostCapabilitiesSection.Exists())
            {
                AddCapabilitiesFromSection(options, jobHostCapabilitiesSection);
            }
        }

        /// <summary>
        /// Adds capabilities from a configuration section.
        /// </summary>
        /// <param name="options">The options to add capabilities to.</param>
        /// <param name="section">The configuration section containing capability definitions.</param>
        private void AddCapabilitiesFromSection(
            AppCapabilitiesOptions options,
            IConfigurationSection section)
        {
            foreach (var child in section.GetChildren())
            {
                var capabilityName = child.Key;
                var capabilityValue = child.Value;

                options.Capabilities[capabilityName] = capabilityValue ?? string.Empty;
            }
        }
    }
}