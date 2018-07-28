// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;
using static Microsoft.Azure.WebJobs.Script.EnvironmentSettingNames;

namespace Microsoft.Azure.WebJobs.Script
{
    internal static class EnvironmentExtensions
    {
        public static bool IsAppServiceEnvironment(this IEnvironment environment)
        {
            return !string.IsNullOrEmpty(environment.GetEnvironmentVariable(AzureWebsiteInstanceId));
        }

        public static bool IsLinuxContainerEnvironment(this IEnvironment environment)
        {
            return !environment.IsAppServiceEnvironment() && !string.IsNullOrEmpty(environment.GetEnvironmentVariable(ContainerName));
        }

        public static bool IsRemoteDebuggingEnabled(this IEnvironment environment)
        {
            return !string.IsNullOrEmpty(environment.GetEnvironmentVariable(RemoteDebuggingPort));
        }

        /// <summary>
        /// Gets a value that uniquely identifies the site and slot.
        /// </summary>
        public static string GetAzureWebsiteUniqueSlotName(this IEnvironment environment)
        {
            string name = environment.GetEnvironmentVariable(AzureWebsiteName);
            string slotName = environment.GetEnvironmentVariable(AzureWebsiteSlotName);

            if (!string.IsNullOrEmpty(slotName) &&
                !string.Equals(slotName, ScriptConstants.DefaultProductionSlotName, StringComparison.OrdinalIgnoreCase))
            {
                name += $"-{slotName}";
            }

            return name?.ToLowerInvariant();
        }
    }
}
