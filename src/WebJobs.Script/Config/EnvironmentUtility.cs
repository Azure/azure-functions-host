// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;
using static Microsoft.Azure.WebJobs.Script.EnvironmentSettingNames;
using static System.Environment;

namespace Microsoft.Azure.WebJobs.Script.Config
{
    /// <summary>
    /// Utility that exposes configuration options defined as environment variables.
    /// </summary>
    internal class EnvironmentUtility
    {
        public static bool IsAppServiceEnvironment => !string.IsNullOrEmpty(GetEnvironmentVariable(AzureWebsiteInstanceId));

        public static bool IsLinuxContainerEnvironment => !IsAppServiceEnvironment && !string.IsNullOrEmpty(GetEnvironmentVariable(ContainerName));

        /// <summary>
        /// Gets a value that uniquely identifies the site and slot.
        /// </summary>
        public static string AzureWebsiteUniqueSlotName
        {
            get
            {
                string name = GetEnvironmentVariable(AzureWebsiteName);
                string slotName = GetEnvironmentVariable(AzureWebsiteSlotName);

                if (!string.IsNullOrEmpty(slotName) &&
                    !string.Equals(slotName, ScriptConstants.DefaultProductionSlotName, StringComparison.OrdinalIgnoreCase))
                {
                    name += $"-{slotName}";
                }

                return name?.ToLowerInvariant();
            }
        }
    }
}
