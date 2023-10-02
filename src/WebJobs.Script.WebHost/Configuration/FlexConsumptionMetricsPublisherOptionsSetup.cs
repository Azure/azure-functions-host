// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Configuration
{
    public class FlexConsumptionMetricsPublisherOptionsSetup : IConfigureOptions<FlexConsumptionMetricsPublisherOptions>
    {
        private IEnvironment _environment;

        public FlexConsumptionMetricsPublisherOptionsSetup(IEnvironment environment)
        {
            _environment = environment;
        }

        public void Configure(FlexConsumptionMetricsPublisherOptions options)
        {
            options.MetricsFilePath = _environment.GetEnvironmentVariable(EnvironmentSettingNames.FunctionsMetricsPublishPath);
        }
    }
}
