// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

#nullable enable

namespace Microsoft.Azure.WebJobs.Script.WebHost.AppCapabilities
{
    internal sealed class ConfigurationProvidedAppCapabilitiesSetup(IConfiguration configuration) : IConfigureOptions<AppCapabilitiesOptions>
    {
        private readonly IConfiguration _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

        /// <summary>
        /// Configures the <see cref="AppCapabilitiesOptions"/> by reading from all available configuration sources.
        /// </summary>
        /// <param name="options">The options instance to configure.</param>
        public void Configure(AppCapabilitiesOptions options)
        {
            // Read from host.json under AzureFunctionsJobHost:capabilities section
            var jobHostCapabilitiesSection = _configuration.GetSection("AzureFunctionsJobHost:appCapabilities");
            if (jobHostCapabilitiesSection.Exists())
            {
                AddCapabilitiesFromSection(options, jobHostCapabilitiesSection, CapabilitySourceNames.ConfigSource);
            }

            // Read from app settings/environment variables under AzureFunctions:Capabilities section
            var appCapabilitiesSection = _configuration.GetSection("AzureFunctions:appCapabilities");
            if (appCapabilitiesSection.Exists())
            {
                AddCapabilitiesFromSection(options, appCapabilitiesSection, CapabilitySourceNames.ConfigSource);
            }

            // Read individual environment variables with FUNCTIONS_CAPABILITY_ prefix
            // TODO: Define how to use environment variables
            // AddCapabilitiesFromEnvironmentVariables(options);
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
        /// Adds capabilities from environment variables with the <c>FUNCTIONS_CAPABILITY_</c> prefix.
        /// </summary>
        /// <param name="options">The options to add capabilities to.</param>
        private void AddCapabilitiesFromEnvironmentVariables(AppCapabilitiesOptions options)
        {
            const string prefix = "FUNCTIONS_CAPABILITY_";

            foreach (var kvp in _configuration.AsEnumerable())
            {
                if (kvp.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && kvp.Value is not null)
                {
                    var capabilityName = kvp.Key[prefix.Length..];
                    AddOrUpdateCapability(
                        options,
                        capabilityName,
                        CapabilitySourceNames.ConfigSource,
                        version: kvp.Value);
                }
            }
        }

        /// <summary>
        /// Reads metadata from a configuration section.
        /// </summary>
        /// <param name="section">The configuration section containing metadata.</param>
        /// <returns>A dictionary containing the metadata key-value pairs.</returns>
        private static System.Collections.Generic.Dictionary<string, object?> ReadMetadata(IConfigurationSection section)
        {
            var metadata = new System.Collections.Generic.Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            foreach (var metadataChild in section.GetChildren())
            {
                metadata[metadataChild.Key] = metadataChild.Value;
            }

            return metadata;
        }

        /// <summary>
        /// Merges metadata dictionaries, with incoming values overriding existing ones.
        /// </summary>
        /// <param name="existing">The existing metadata dictionary.</param>
        /// <param name="incoming">The incoming metadata dictionary (optional).</param>
        /// <returns>A merged dictionary containing all metadata.</returns>
        private static System.Collections.Generic.Dictionary<string, object?> MergeMetadata(
            System.Collections.Generic.IDictionary<string, object?> existing,
            System.Collections.Generic.Dictionary<string, object?>? incoming)
        {
            if (incoming is null || incoming.Count == 0)
            {
                return new System.Collections.Generic.Dictionary<string, object?>(existing);
            }

            var merged = new System.Collections.Generic.Dictionary<string, object?>(existing, StringComparer.OrdinalIgnoreCase);
            foreach (var (k, v) in incoming)
            {
                merged[k] = v;
            }

            return merged;
        }

        private static void AddOrUpdateCapability(
            AppCapabilitiesOptions options,
            string name,
            string source,
            string? version = null,
            Dictionary<string, object?>? metadata = null)
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