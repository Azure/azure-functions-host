// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.ApplicationInsights.WindowsServer.Channel.Implementation;
using Microsoft.Azure.WebJobs.Logging.ApplicationInsights;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Configuration
{
    internal class ApplicationInsightsLoggerOptionsSetup : IConfigureOptions<ApplicationInsightsLoggerOptions>
    {
        private readonly IConfiguration _configuration;
        private readonly IEnvironment _environment;

        public ApplicationInsightsLoggerOptionsSetup(ILoggerProviderConfiguration<ApplicationInsightsLoggerProvider> configuration, IEnvironment environment)
        {
            _configuration = configuration.Configuration;
            _environment = environment;
        }

        public void Configure(ApplicationInsightsLoggerOptions options)
        {
            // Initialize sampling with a non-default MaxTelemetryItemsPerSecond. This will
            // be removed if sampling is disabled.
            options.SamplingSettings = new SamplingPercentageEstimatorSettings
            {
                MaxTelemetryItemsPerSecond = 20
            };

            // SnapshotConfiguration will be null by default. The presence of a SnapshotConfiguration section in
            // IConfiguration will cause the SnapshotConfiguration to be created and the TelemetryProcessor to be applied.
            _configuration.Bind(options);

            ApplySamplingSettings(options);

            string quickPulseKey = _environment.GetEnvironmentVariable(EnvironmentSettingNames.AppInsightsQuickPulseAuthApiKey);
            if (!string.IsNullOrEmpty(quickPulseKey))
            {
                options.QuickPulseAuthenticationApiKey = quickPulseKey;
            }
        }

        private void ApplySamplingSettings(ApplicationInsightsLoggerOptions options)
        {
            // Sampling settings do not have a built-in "IsEnabled" value, so we are making our own.
            string samplingPath = nameof(ApplicationInsightsLoggerOptions.SamplingSettings);
            bool samplingEnabled = _configuration.GetSection(samplingPath).GetValue("IsEnabled", true);

            if (!samplingEnabled)
            {
                options.SamplingSettings = null;
                return;
            }

            // Excluded/Included types must be moved from SamplingSettings to their respective properties in logger options
            options.SamplingExcludedTypes = _configuration.GetSection(samplingPath).GetValue<string>("ExcludedTypes", null);
            options.SamplingIncludedTypes = _configuration.GetSection(samplingPath).GetValue<string>("IncludedTypes", null);
        }
    }
}
