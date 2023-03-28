// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Scale;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Configuration
{
    internal class ScaleOptionsSetup : IConfigureOptions<ScaleOptions>
    {
        private readonly IEnvironment _environment;

        public ScaleOptionsSetup(IEnvironment environment)
        {
            _environment = environment;
        }

        public void Configure(ScaleOptions options)
        {
            options.IsRuntimeScalingEnabled = _environment.IsRuntimeScaleMonitoringEnabled();
            options.IsTargetScalingEnabled = _environment.IsTargetBasedScalingEnabled();
        }
    }
}
