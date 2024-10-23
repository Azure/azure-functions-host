// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;

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
            string featureFlags = environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags);
            if (!string.IsNullOrEmpty(featureFlags))
            {
                string[] flags = featureFlags.Split(',');
                return flags.Contains(name, StringComparer.OrdinalIgnoreCase);
            }

            return false;
        }
    }
}
