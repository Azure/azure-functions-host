// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Scale;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Config
{
    public class ScaleOptionsSetup : IConfigureOptions<ScaleOptions>
    {
        private readonly IOptions<FunctionsHostingConfigOptions> _functionsHostingConfigOptions;
        private readonly IEnvironment _environment;

        public ScaleOptionsSetup(IEnvironment environment, IOptions<FunctionsHostingConfigOptions> functionsHostingConfigOptions)
        {
            _environment = environment;
            _functionsHostingConfigOptions = functionsHostingConfigOptions;
        }

        public void Configure(ScaleOptions options)
        {
            options.IsTargetBasedScalingEnabled = _environment.IsTargetBasedScalingEnabled();
            options.IsTargetBasedScalingEnabledForTriggerFunc = IsTargetBasedScalingEnabledForTrigger;
        }

        private bool IsTargetBasedScalingEnabledForTrigger(ITargetScaler targetScaler)
        {
            string assemblyName = targetScaler.GetType().Assembly.GetName().Name;
            string flag = _functionsHostingConfigOptions.Value.GetFeature(assemblyName);
            return flag == "1";
        }
    }
}
