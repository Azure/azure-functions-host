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

        public FunctionsHostingConfigOptionsSetup(IConfiguration configuration)
        {
            _configuration = configuration;
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
            if (options.RestrictHostLogs == true && !FeatureFlags.IsEnabled(ScriptConstants.FeatureFlagEnableHostLogs))
            {
                ScriptLoggingBuilderExtensions.SystemLogCategoryPrefixes = ScriptConstants.RestrictedSystemLogCategoryPrefixes;
            }
        }
    }
}
