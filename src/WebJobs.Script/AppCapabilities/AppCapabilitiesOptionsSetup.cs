// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

#nullable enable

namespace Microsoft.Azure.WebJobs.Script.AppCapabilities
{
    internal sealed class AppCapabilitiesOptionsSetup : IConfigureOptions<AppCapabilitiesOptions>
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<AppCapabilitiesOptionsSetup> _logger;
        private readonly IAppCapabilitiesStore _appCapabilitiesStore;

        public AppCapabilitiesOptionsSetup(
            IConfiguration configuration,
            IAppCapabilitiesStore appCapabilitiesStore,
            ILogger<AppCapabilitiesOptionsSetup> logger)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _appCapabilitiesStore = appCapabilitiesStore ?? throw new ArgumentNullException(nameof(appCapabilitiesStore));
        }

        /// <summary>
        /// Configures the <see cref="AppCapabilitiesOptions"/> by reading from known configuration sources.
        /// </summary>
        /// <param name="options">The options instance to configure.</param>
        public void Configure(AppCapabilitiesOptions options)
        {
            var capabilitiesSection = _configuration.GetSection(ConfigurationSectionNames.AppCapabilities);
            if (capabilitiesSection.Exists())
            {
                AddCapabilitiesFromSection(options, capabilitiesSection);
            }

            foreach (var kvp in _appCapabilitiesStore.Capabilities)
            {
                options.Capabilities[kvp.Key] = kvp.Value;
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
            var children = section.GetChildren();
            _logger.LogDebug("Loading App Capabilities from configuration section '{sectionName}' with {count} entries.",
                section.Path, children.Count());

            foreach (var child in children)
            {
                options.Capabilities[child.Key] = child.Value ?? string.Empty;
            }
        }
    }
}