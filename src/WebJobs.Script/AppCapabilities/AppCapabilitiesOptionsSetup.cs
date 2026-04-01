// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
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
        private readonly IAppCapabilitiesStore _appCapabilitiesStore;
        private readonly ILogger<AppCapabilitiesOptionsSetup> _logger;

        public AppCapabilitiesOptionsSetup(
            IConfiguration configuration,
            IAppCapabilitiesStore appCapabilitiesStore,
            ILogger<AppCapabilitiesOptionsSetup> logger)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _appCapabilitiesStore = appCapabilitiesStore ?? throw new ArgumentNullException(nameof(appCapabilitiesStore));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Configures the <see cref="AppCapabilitiesOptions"/> by reading from known configuration sources.
        /// Reads from host.json first, followed by worker-provided capabilities.
        /// Worker-provided capabilities will override any duplicates from configuration.
        /// </summary>
        /// <param name="options">The options instance to configure.</param>
        public void Configure(AppCapabilitiesOptions options)
        {
            var capabilitiesSection = _configuration.GetSection(ConfigurationSectionNames.AppCapabilities);
            if (capabilitiesSection.Exists())
            {
                AddCapabilitiesFromSection(options, capabilitiesSection);
            }

            try
            {
                foreach (var kvp in _appCapabilitiesStore.Capabilities)
                {
                    if (options.ContainsKey(kvp.Key))
                    {
                        _logger.LogDebug("Duplicate capability key found. Overriding existing value with a worker provided value.");
                    }
                    options[kvp.Key] = kvp.Value;
                }
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogDebug(ex, "App capabilities store has not been initialized. Using configuration values only.");
            }
        }

        /// <summary>
        /// Adds capabilities from a configuration section.
        /// </summary>
        /// <param name="options">The options to add capabilities to.</param>
        /// <param name="section">The configuration section containing capability definitions.</param>
        private void AddCapabilitiesFromSection(
            IDictionary<string, string> options,
            IConfigurationSection section)
        {
            foreach (var child in section.GetChildren())
            {
                if (child.Value is not null)
                {
                    if (options.ContainsKey(child.Key))
                    {
                        _logger.LogDebug("Duplicate capability key found. Overriding existing value with a configuration provided value.");
                    }

                    options[child.Key] = child.Value;
                }
            }
        }
    }
}
