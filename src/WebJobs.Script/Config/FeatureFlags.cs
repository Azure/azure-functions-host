// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Extensions;

namespace Microsoft.Azure.WebJobs.Script.Config
{
    /// <summary>
    /// Provides a way to determine whether certain runtime features are enabled or disabled
    /// based on a feature flags app setting.
    /// </summary>
    public static class FeatureFlags
    {
        public static bool IsEnabled(string name) => IsEnabled(name, SystemEnvironment.Instance);

        public static bool IsEnabled(string name, IEnvironment environment)
        {
            var featureFlags = environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags);

            if (string.IsNullOrEmpty(featureFlags))
            {
                return false;
            }

            return featureFlags.ContainsToken(name, separator: ',', StringComparison.OrdinalIgnoreCase);
        }
    }
}