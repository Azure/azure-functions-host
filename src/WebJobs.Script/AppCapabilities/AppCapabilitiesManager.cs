// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

#nullable enable

namespace Microsoft.Azure.WebJobs.Script.AppCapabilities
{
    internal sealed class AppCapabilitiesManager(IConfiguration configuration) : IConfigureOptions<AppCapabilitiesOptions>
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
                AddCapabilitiesFromSection(options, jobHostCapabilitiesSection, CapabilitySourceNames.ConfigSource);
            }
        }

        /// <summary>
        /// Adds capabilities from a configuration section.
        /// </summary>
        /// <param name="options">The options to add capabilities to.</param>
        /// <param name="section">The configuration section containing capability definitions.</param>
        /// <param name="source">The source name for these capabilities.</param>
        private void AddCapabilitiesFromSection(
            AppCapabilitiesOptions options,
            IConfigurationSection section,
            string source)
        {
            foreach (var child in section.GetChildren())
            {
                var capabilityName = child.Key;
                var version = child["version"];
                var metadata = ReadMetadata(child);

                AddOrUpdateCapability(options, capabilityName, source, version, metadata);
            }
        }

        /// <summary>
        /// Reads metadata from a configuration section.
        /// </summary>
        /// <param name="section">The configuration section containing metadata.</param>
        /// <returns>A dictionary containing the metadata key-value pairs.</returns>
        private static Dictionary<string, string> ReadMetadata(IConfigurationSection section)
        {
            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var metadataChild in section.GetChildren())
            {
                metadata[metadataChild.Key] = metadataChild.Value;
            }

            return metadata;
        }

        internal static void AddOrUpdateCapability(
            AppCapabilitiesOptions options,
            string name,
            string source,
            string? version = null,
            Dictionary<string, string>? metadata = null)
        {
            AppCapabilityHelpers.AddOrUpdateCapability(
                options.Capabilities,
                name,
                source,
                version,
                metadata);
        }
    }
}