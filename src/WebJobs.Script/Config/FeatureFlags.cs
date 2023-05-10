// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.AppService.Proxy.Common.Extensions;

namespace Microsoft.Azure.WebJobs.Script.Config
{
    /// <summary>
    /// Provides a way to determine whether certain runtime features are enabled or disabled
    /// based on a feature flags app setting.
    /// </summary>
    public static class FeatureFlags
    {
        private static readonly object _cacheLock = new();
        private static HashSet<string> _featureFlags;

        // for testing
        internal static HashSet<string> InternalCache
        {
            get { return _featureFlags; }
            set { _featureFlags = value; }
        }

        public static bool IsEnabled(string name) => IsEnabled(name, SystemEnvironment.Instance);

        public static bool IsEnabled(string name, IEnvironment environment)
        {
            if (_featureFlags is not null)
            {
                return _featureFlags.Contains(name);
            }

            var flags = GetFeatureFlags(environment);

            if (environment.IsPlaceholderModeEnabled())
            {
                // if in placeholder mode, do not cache
                return flags.Contains(name, StringComparer.OrdinalIgnoreCase);
            }

            // initialize the cache if not in placeholder mode
            lock (_cacheLock)
            {
                if (_featureFlags is null)
                {
                    var featureFlagsTemp = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    featureFlagsTemp.AddRange(flags);

                    _featureFlags = featureFlagsTemp;
                }
            }

            return _featureFlags.Contains(name);
        }

        private static string[] GetFeatureFlags(IEnvironment environment)
        {
            string featureFlags = environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags) ?? string.Empty;
            return featureFlags.Split(',');
        }
    }
}