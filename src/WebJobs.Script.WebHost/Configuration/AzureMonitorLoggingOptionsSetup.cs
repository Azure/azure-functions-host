// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Configuration
{
    public class AzureMonitorLoggingOptionsSetup : IConfigureOptions<AzureMonitorLoggingOptions>
    {
        private readonly IConfiguration _configuration;

        public AzureMonitorLoggingOptionsSetup(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public void Configure(AzureMonitorLoggingOptions options)
        {
            var isAzureMonitorTimeIsoFormatEnabled = _configuration.GetValue<bool?>(EnvironmentSettingNames.AzureMonitorTimeIsoFormatEnabled);
            options.IsAzureMonitorTimeIsoFormatEnabled = isAzureMonitorTimeIsoFormatEnabled ?? false;
        }
    }
}
