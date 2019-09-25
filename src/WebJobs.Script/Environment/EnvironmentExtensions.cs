// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Runtime.InteropServices;
using static Microsoft.Azure.WebJobs.Script.EnvironmentSettingNames;

namespace Microsoft.Azure.WebJobs.Script
{
    internal static class EnvironmentExtensions
    {
        // For testing
        internal static string BaseDirectory { get; set; }

        public static string GetEnvironmentVariableOrDefault(this IEnvironment environment, string name, string defaultValue)
        {
            return environment.GetEnvironmentVariable(name) ?? defaultValue;
        }

        public static bool IsAppServiceEnvironment(this IEnvironment environment)
        {
            return !string.IsNullOrEmpty(environment.GetEnvironmentVariable(AzureWebsiteInstanceId));
        }

        public static bool IsLinuxContainerEnvironment(this IEnvironment environment)
        {
            return !environment.IsAppServiceEnvironment() && !string.IsNullOrEmpty(environment.GetEnvironmentVariable(ContainerName));
        }

        public static bool IsLinuxMetricsPublishingEnabled(this IEnvironment environment)
        {
            return environment.IsLinuxContainerEnvironment() && string.IsNullOrEmpty(environment.GetEnvironmentVariable(ContainerStartContext));
        }

        public static bool IsLinuxAppServiceEnvironment(this IEnvironment environment)
        {
            return environment.IsAppServiceEnvironment() && !string.IsNullOrEmpty(environment.GetEnvironmentVariable(FunctionsLogsMountPath));
        }

        public static bool IsLinuxHostingEnvironment(this IEnvironment environment)
        {
            return environment.IsLinuxContainerEnvironment() || environment.IsLinuxAppServiceEnvironment();
        }

        public static bool IsPlaceholderModeEnabled(this IEnvironment environment)
        {
            return environment.GetEnvironmentVariable(AzureWebsitePlaceholderMode) == "1";
        }

        public static bool IsLegacyPlaceholderTemplateSite(this IEnvironment environment)
        {
            string siteName = environment.GetEnvironmentVariable(AzureWebsiteName);
            return string.IsNullOrEmpty(siteName) ? false : siteName.Equals(ScriptConstants.LegacyPlaceholderTemplateSiteName, StringComparison.InvariantCultureIgnoreCase);
        }

        public static bool IsEasyAuthEnabled(this IEnvironment environment)
        {
            bool.TryParse(environment.GetEnvironmentVariable(EnvironmentSettingNames.EasyAuthEnabled), out bool isEasyAuthEnabled);
            return isEasyAuthEnabled;
        }

        public static bool IsRunningAsHostedSiteExtension(this IEnvironment environment)
        {
            if (environment.IsAppServiceEnvironment())
            {
                string siteExtensionsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "SiteExtensions", "Functions");
                return (BaseDirectory ?? AppContext.BaseDirectory).StartsWith(siteExtensionsPath, StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        public static bool IsRemoteDebuggingEnabled(this IEnvironment environment)
        {
            return !string.IsNullOrEmpty(environment.GetEnvironmentVariable(RemoteDebuggingPort));
        }

        public static bool IsZipDeployment(this IEnvironment environment)
        {
            // Run From Package app setting exists
            return IsValidZipSetting(environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteZipDeployment)) ||
                IsValidZipSetting(environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteAltZipDeployment)) ||
                IsValidZipSetting(environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteRunFromPackage)) ||
                IsValidZipUrl(environment.GetEnvironmentVariable(EnvironmentSettingNames.ScmRunFromPackage));
        }

        public static bool IsValidZipSetting(string appSetting)
        {
            // valid values are 1 or an absolute URI
            return string.Equals(appSetting, "1") || IsValidZipUrl(appSetting);
        }

        public static bool IsValidZipUrl(string appSetting)
        {
            return Uri.TryCreate(appSetting, UriKind.Absolute, out Uri result);
        }

        public static bool IsAppServiceWindowsEnvironment(this IEnvironment environment)
        {
            return environment.IsAppServiceEnvironment() && RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        }

        public static bool IsCoreToolsEnvironment(this IEnvironment environment)
        {
            return !string.IsNullOrEmpty(environment.GetEnvironmentVariable(CoreToolsEnvironment));
        }

        public static bool IsContainerEnvironment(this IEnvironment environment)
        {
            var runningInContainer = environment.GetEnvironmentVariable(RunningInContainer);
            return !string.IsNullOrEmpty(runningInContainer)
                && bool.TryParse(runningInContainer, out bool runningInContainerValue)
                && runningInContainerValue;
        }

        public static bool IsPersistentFileSystemAvailable(this IEnvironment environment)
        {
            return environment.IsAppServiceWindowsEnvironment()
                || environment.IsLinuxAppServiceEnvWithPersistentFileSystem()
                || environment.IsCoreToolsEnvironment();
        }

        public static bool IsLinuxAppServiceEnvWithPersistentFileSystem(this IEnvironment environment)
        {
            if (environment.IsLinuxAppServiceEnvironment())
            {
                string storageConfig = environment.GetEnvironmentVariable(LinuxAzureAppServiceStorage);

                // AzureAppServiceStorage is enabled by default, So return if true it is not set
                if (string.IsNullOrEmpty(storageConfig))
                {
                    return true;
                }
                return bool.TryParse(storageConfig, out bool storageConfigValue) && storageConfigValue;
            }
            return false;
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

        /// <summary>
        /// Gets a the subscription Id of the current site.
        /// </summary>
        public static string GetSubscriptionId(this IEnvironment environment)
        {
            string ownerName = environment.GetEnvironmentVariable(AzureWebsiteOwnerName) ?? string.Empty;
            if (!string.IsNullOrEmpty(ownerName))
            {
                int idx = ownerName.IndexOf('+');
                if (idx > 0)
                {
                    return ownerName.Substring(0, idx);
                }
            }

            return null;
        }

        public static bool IsContainerReady(this IEnvironment environment)
        {
            return !string.IsNullOrEmpty(environment.GetEnvironmentVariable(AzureWebsiteContainerReady));
        }

        public static string GetKubernetesApiServerUrl(this IEnvironment environment)
        {
            string host = environment.GetEnvironmentVariable(KubernetesServiceHost);
            string port = environment.GetEnvironmentVariable(KubernetesServiceHttpsPort);

            if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(port))
            {
                throw new InvalidOperationException($"Both {KubernetesServiceHost} and {KubernetesServiceHttpsPort} are required for {nameof(GetKubernetesApiServerUrl)}.");
            }

            return $"https://{host}:{port}";
        }

        public static bool IsMountEnabled(this IEnvironment environment)
            => string.Equals(environment.GetEnvironmentVariable(MountEnabled), "1") &&
            !string.IsNullOrEmpty(environment.GetEnvironmentVariable(MeshInitURI));

        public static bool IsMountDisabled(this IEnvironment environment)
            => string.Equals(environment.GetEnvironmentVariable(MountEnabled), "0") ||
            string.IsNullOrEmpty(environment.GetEnvironmentVariable(MeshInitURI));
    }
}
