// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.ApplicationInsights.SnapshotCollector;
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

        public ApplicationInsightsLoggerOptionsSetup(ILoggerProviderConfiguration<ApplicationInsightsLoggerProvider> configuration)
        {
            _configuration = configuration.Configuration;
        }

        public void Configure(ApplicationInsightsLoggerOptions options)
        {
            // Make sure we have snapshot enabled by default. Config can set the "IsEnabled" property to disable.
            options.SnapshotConfiguration = new SnapshotCollectorConfiguration();

            _configuration.Bind(options);

            // Sampling settings do not have a built-in "IsEnabled" value, so we are making our own.
            ConfigureSampling(options);
        }

        private void ConfigureSampling(ApplicationInsightsLoggerOptions options)
        {
            string samplingPath = nameof(ApplicationInsightsLoggerOptions.SamplingSettings);
            bool samplingEnabled = _configuration.GetSection(samplingPath).GetValue("IsEnabled", true);

            if (samplingEnabled)
            {
                // If the config had values set up, the call to Bind() will create this for us.
                // If it's still null, we want to make sure we initialize it to default values.
                if (options.SamplingSettings == null)
                {
                    options.SamplingSettings = new SamplingPercentageEstimatorSettings();
                }
            }
            else
            {
                options.SamplingSettings = null;
            }
        }
    }
}
