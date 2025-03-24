// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Configuration
{
    public class LinuxConsumptionLegionMetricsPublisherOptionsSetup : IConfigureOptions<LinuxConsumptionLegionMetricsPublisherOptions>
    {
        private readonly IEnvironment _environment;

        public LinuxConsumptionLegionMetricsPublisherOptionsSetup(IEnvironment environment)
        {
            _environment = environment;
        }

        public void Configure(LinuxConsumptionLegionMetricsPublisherOptions options)
        {
            options.ContainerName = _environment.GetEnvironmentVariable(EnvironmentSettingNames.ContainerName);
            options.MetricsFilePath = _environment.GetEnvironmentVariable(EnvironmentSettingNames.FunctionsMetricsPublishPath);
        }
    }
}