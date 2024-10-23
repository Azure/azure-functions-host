// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Config
{
    internal class FunctionsHostingConfigOptionsSetup : IConfigureOptions<FunctionsHostingConfigOptions>
    {
        private readonly IConfiguration _configuration;
        private readonly IEnvironment _environment;

        public FunctionsHostingConfigOptionsSetup(IConfiguration configuration, IEnvironment environment)
        {
            _configuration = configuration;
            _environment = environment;
        }

        public void Configure(FunctionsHostingConfigOptions options)
        {
            IConfigurationSection section = _configuration.GetSection(ScriptConstants.FunctionsHostingConfigSectionName);
            if (section is not null)
            {
                foreach (var pair in section.GetChildren())
                {
                    if (!string.IsNullOrEmpty(pair.Value))
                    {
                        options.Features[pair.Key] = pair.Value;
                    }
                }
            }

            // Restrict non-critial logs post-configuration/startup
            ConfigureHostLogs(options);
        }

        private void ConfigureHostLogs(FunctionsHostingConfigOptions options)
        {
            // Feature flag should take precedence over the host configuration
            if (FeatureFlags.IsEnabled(ScriptConstants.FeatureFlagEnableHostLogs, _environment))
            {
                return;
            }

            if (options.RestrictHostLogs)
            {
                ScriptLoggingBuilderExtensions.SystemLogCategoryPrefixes = ScriptConstants.RestrictedSystemLogCategoryPrefixes;
            }
        }
    }
}
