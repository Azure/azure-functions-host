// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

#nullable enable

namespace Microsoft.Azure.WebJobs.Script.AppCapabilities
{
    internal sealed class AppCapabilitiesOptionsSetup : IConfigureOptions<AppCapabilitiesOptions>
    {
        private readonly IConfiguration _configuration;
        private readonly IAppCapabilitiesStore _appCapabilitiesStore;

        public AppCapabilitiesOptionsSetup(
            IConfiguration configuration,
            IAppCapabilitiesStore appCapabilitiesStore)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _appCapabilitiesStore = appCapabilitiesStore ?? throw new ArgumentNullException(nameof(appCapabilitiesStore));
        }

        /// <summary>
        /// Configures the <see cref="AppCapabilitiesOptions"/> by reading from known configuration sources.
        /// </summary>
        /// <param name="options">The options instance to configure.</param>
        public void Configure(AppCapabilitiesOptions options)
        {
            var optionsDict = (IDictionary<string, string>)options;

            var capabilitiesSection = _configuration.GetSection(ConfigurationSectionNames.AppCapabilities);
            if (capabilitiesSection.Exists())
            {
                AddCapabilitiesFromSection(optionsDict, capabilitiesSection);
            }

            foreach (var kvp in _appCapabilitiesStore.Capabilities)
            {
                optionsDict[kvp.Key] = kvp.Value;
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
                    options[child.Key] = child.Value;
                }
            }
        }
    }
}