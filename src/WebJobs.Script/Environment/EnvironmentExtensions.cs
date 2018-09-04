// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
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

        public static bool IsPlaceholderModeEnabled(this IEnvironment environment)
        {
            return environment.GetEnvironmentVariable(AzureWebsitePlaceholderMode) == "1";
        }

        public static bool IsRemoteDebuggingEnabled(this IEnvironment environment)
        {
            return !string.IsNullOrEmpty(environment.GetEnvironmentVariable(RemoteDebuggingPort));
        }

        public static bool IsZipDeployment(this IEnvironment environment)
        {
            return !string.IsNullOrEmpty(environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteZipDeployment));
        }

        public static bool FileSystemIsReadOnly(this IEnvironment environment)
        {
            return environment.IsZipDeployment();
        }

        /// <summary>
        /// Gets a value indicating whether the application is running in a Dynamic
        /// App Service environment.
        /// </summary>
        /// <param name="environment">The environment to verify</param>
        /// <returns><see cref="true"/> if running in a dynamic App Service app; otherwise, false.</returns>
        public static bool IsDynamic(this IEnvironment environment)
        {
            string value = environment.GetEnvironmentVariable(AzureWebsiteSku);
            return string.Equals(value, ScriptConstants.DynamicSku, StringComparison.OrdinalIgnoreCase);
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

        public static bool IsContainerReady(this IEnvironment environment)
        {
            return !string.IsNullOrEmpty(environment.GetEnvironmentVariable(AzureWebsiteContainerReady));
        }
    }
}
