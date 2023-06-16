// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using static Microsoft.Azure.WebJobs.Script.EnvironmentSettingNames;

namespace Microsoft.Azure.WebJobs.Script
{
    internal static class EnvironmentExtensions
    {
        private static bool? isApplicationInsightsAgentEnabled;

        private static bool? isMultiLanguageEnabled;

        internal static string BaseDirectory { get; set; }

        public static string GetEnvironmentVariableOrDefault(this IEnvironment environment, string name, string defaultValue)
        {
            return environment.GetEnvironmentVariable(name) ?? defaultValue;
        }

        public static bool IsLinuxMetricsPublishingEnabled(this IEnvironment environment)
        {
            return environment.IsLinuxConsumptionOnAtlas() && string.IsNullOrEmpty(environment.GetEnvironmentVariable(ContainerStartContext));
        }

        public static bool IsPlaceholderModeEnabled(this IEnvironment environment)
        {
            return environment.GetEnvironmentVariable(AzureWebsitePlaceholderMode) == "1";
        }

        public static bool UsePlaceholderDotNetIsolated(this IEnvironment environment)
        {
            return environment.GetEnvironmentVariable(AzureWebsiteUsePlaceholderDotNetIsolated) == "1";
        }

        public static bool IsLegacyPlaceholderTemplateSite(this IEnvironment environment)
        {
            string siteName = environment.GetEnvironmentVariable(AzureWebsiteName);
            return string.IsNullOrEmpty(siteName) ? false : siteName.Equals(ScriptConstants.LegacyPlaceholderTemplateSiteName, StringComparison.InvariantCultureIgnoreCase);
        }

        public static bool IsRuntimeScaleMonitoringEnabled(this IEnvironment environment)
        {
            return environment.GetEnvironmentVariable(FunctionsRuntimeScaleMonitoringEnabled) == "1";
        }

        public static bool IsAdminIsolationEnabled(this IEnvironment environment)
        {
            return environment.GetEnvironmentVariable(FunctionsAdminIsolationEnabled) == "1";
        }

        public static bool IsEasyAuthEnabled(this IEnvironment environment)
        {
            bool.TryParse(environment.GetEnvironmentVariable(EasyAuthEnabled), out bool isEasyAuthEnabled);
            return isEasyAuthEnabled;
        }

        /// <summary>
        /// Returns true if any Functions AzureMonitor log categories are enabled.
        /// </summary>
        public static bool IsAzureMonitorEnabled(this IEnvironment environment)
        {
            string value = environment.GetEnvironmentVariable(AzureMonitorCategories);
            if (value == null)
            {
                return true;
            }
            string[] categories = value.Split(',');
            return categories.Contains(ScriptConstants.AzureMonitorTraceCategory);
        }

        /// <summary>
        /// Gets if <a href="https://docs.microsoft.com/azure/azure-functions/functions-proxies">proxies.json support</a> is enabled.
        /// </summary>
        public static bool IsProxiesEnabled(this IEnvironment environment)
        {
            return FeatureFlags.IsEnabled(ScriptConstants.FeatureFlagEnableProxies, environment);
        }

        public static bool IsRunningAsHostedSiteExtension(this IEnvironment environment)
        {
            if (environment.IsAppService())
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

        public static bool ZipDeploymentAppSettingsExist(this IEnvironment environment)
        {
            return !string.IsNullOrEmpty(environment.GetEnvironmentVariable(AzureWebsiteZipDeployment)) ||
                   !string.IsNullOrEmpty(environment.GetEnvironmentVariable(AzureWebsiteAltZipDeployment)) ||
                   !string.IsNullOrEmpty(environment.GetEnvironmentVariable(AzureWebsiteRunFromPackage)) ||
                   !string.IsNullOrEmpty(environment.GetEnvironmentVariable(ScmRunFromPackage));
        }

        public static bool AzureFilesAppSettingsExist(this IEnvironment environment)
        {
            return !string.IsNullOrEmpty(environment.GetEnvironmentVariable(AzureFilesConnectionString)) &&
                   !string.IsNullOrEmpty(environment.GetEnvironmentVariable(AzureFilesContentShare));
        }

        public static string GetAzureWebsiteHomePath(this IEnvironment environment)
        {
            return environment.GetEnvironmentVariable(AzureWebsiteHomePath);
        }

        public static string GetSitePackagesPath(this IEnvironment environment)
        {
            return Path.Combine(environment.GetAzureWebsiteHomePath(), ScriptConstants.DataFolderName, ScriptConstants.SitePackagesFolderName);
        }

        public static string GetSitePackageNameTxtPath(this IEnvironment environment)
        {
            return Path.Combine(environment.GetSitePackagesPath(), ScriptConstants.SitePackageNameTxtFileName);
        }

        public static bool IsCoreTools(this IEnvironment environment)
        {
            return !string.IsNullOrEmpty(environment.GetEnvironmentVariable(CoreToolsEnvironment));
        }

        public static bool IsV2CompatibilityMode(this IEnvironment environment)
        {
            string compatModeString = environment.GetEnvironmentVariable(FunctionsV2CompatibilityModeKey);
            bool.TryParse(compatModeString, out bool isFunctionsV2CompatibilityMode);

            string extensionVersion = environment.GetEnvironmentVariable(FunctionsExtensionVersion);
            bool isV2ExtensionVersion = string.Compare(extensionVersion, "~2", CultureInfo.InvariantCulture, CompareOptions.OrdinalIgnoreCase) == 0;

            return isFunctionsV2CompatibilityMode || isV2ExtensionVersion;
        }

        public static bool IsV2CompatibleOnV3Extension(this IEnvironment environment)
        {
            string compatModeString = environment.GetEnvironmentVariable(FunctionsV2CompatibilityModeKey);
            bool.TryParse(compatModeString, out bool isFunctionsV2CompatibilityMode);

            string extensionVersion = environment.GetEnvironmentVariable(FunctionsExtensionVersion);
            bool isV3ExtensionVersion = string.Compare(extensionVersion, "~3", CultureInfo.InvariantCulture, CompareOptions.OrdinalIgnoreCase) == 0;

            return isFunctionsV2CompatibilityMode && isV3ExtensionVersion;
        }

        public static bool IsContainer(this IEnvironment environment)
        {
            var runningInContainer = environment.GetEnvironmentVariable(RunningInContainer);
            return !string.IsNullOrEmpty(runningInContainer)
                && bool.TryParse(runningInContainer, out bool runningInContainerValue)
                && runningInContainerValue;
        }

        public static bool IsPersistentFileSystemAvailable(this IEnvironment environment)
        {
            return environment.IsWindowsAzureManagedHosting()
                || environment.IsLinuxAppServiceWithPersistentFileSystem()
                || environment.IsCoreTools();
        }

        public static bool IsLinuxAppServiceWithPersistentFileSystem(this IEnvironment environment)
        {
            if (environment.IsLinuxAppService())
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

        /// <summary>
        /// Gets a value indicating whether the application is running in a Windows Consumption (dynamic)
        /// App Service environment.
        /// </summary>
        /// <param name="environment">The environment to verify.</param>
        /// <returns><see cref="true"/> if running in a Windows Consumption App Service app; otherwise, false.</returns>
        public static bool IsWindowsConsumption(this IEnvironment environment)
        {
            string value = environment.GetEnvironmentVariable(AzureWebsiteSku);
            return string.Equals(value, ScriptConstants.DynamicSku, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Gets a value indicating whether the application is running in one of our consumption SKUs (e.g.  Windows or Linux Consumption (Dynamic),
        /// or Flex Consumption).
        /// App Service environment.
        /// </summary>
        /// <param name="environment">The environment to verify.</param>
        /// <returns><see cref="true"/> if running in one of our Consumption SKUs, false otherwise.</returns>
        public static bool IsConsumptionSku(this IEnvironment environment)
        {
            return IsWindowsConsumption(environment) || IsAnyLinuxConsumption(environment) || IsFlexConsumptionSku(environment);
        }

        /// <summary>
        /// Gets a value indicating whether the application is running in the Flex Consumption Sku.
        /// </summary>
        /// <param name="environment">The environment to verify.</param>
        /// <returns><see cref="true"/> if running in the FlexConsumption Sku, false otherwise.</returns>
        public static bool IsFlexConsumptionSku(this IEnvironment environment)
        {
            string value = environment.GetEnvironmentVariable(AzureWebsiteSku);
            if (string.Equals(value, ScriptConstants.FlexConsumptionSku, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // when in placeholder mode, site settings like SKU are not available
            // to enable this check to run in both modes, we check additional settings
            return environment.IsLinuxConsumptionOnLegion();
        }

        /// <summary>
        /// Returns true if the app is running on Virtual Machine Scale Sets (VMSS).
        /// </summary>
        public static bool IsVMSS(this IEnvironment environment)
        {
            string value = environment.GetEnvironmentVariable(EnvironmentSettingNames.RoleInstanceId);
            return value != null && value.IndexOf("HostRole", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Gets the number of effective cores taking into account SKU/environment restrictions.
        /// </summary>
        public static int GetEffectiveCoresCount(this IEnvironment environment)
        {
            // When not running on VMSS, the dynamic plan has some limits that mean that a given instance is using effectively a single core,
            // so we should not use Environment.Processor count in this case.
            var effectiveCores = (environment.IsWindowsConsumption() && !environment.IsVMSS()) ? 1 : Environment.ProcessorCount;
            return effectiveCores;
        }

        /// <summary>
        /// Gets a value indicating whether the application is running in a Windows Elastic Premium
        /// App Service environment.
        /// </summary>
        /// <param name="environment">The environment to verify.</param>
        /// <returns><see cref="true"/> if running in a Windows Elastic Premium app; otherwise, false.</returns>
        public static bool IsWindowsElasticPremium(this IEnvironment environment)
        {
            string value = environment.GetEnvironmentVariable(AzureWebsiteSku);
            return string.Equals(value, ScriptConstants.ElasticPremiumSku, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsDynamicSku(this IEnvironment environment)
        {
            return environment.IsConsumptionSku() || environment.IsWindowsElasticPremium();
        }

        /// <summary>
        /// Gets a value indicating whether the application is running in an Azure Windows managed hosting environment
        /// (i.e. Windows Consumption or Windows Dedicated).
        /// </summary>
        /// <param name="environment">The environment to verify.</param>
        /// <returns><see cref="true"/> if running in a Windows Azure managed hosting environment; otherwise, false.</returns>
        public static bool IsWindowsAzureManagedHosting(this IEnvironment environment)
        {
            return environment.IsAppService() && RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        }

        /// <summary>
        /// Gets a value indicating whether the application is running in Kubernetes Environment.
        /// </summary>
        /// <param name="environment">The environment to verify.</param>
        /// <returns><see cref="true"/> if running in Kubernetes environment; otherwise, false.</returns>
        public static bool IsAnyKubernetesEnvironment(this IEnvironment environment)
        {
            return environment.IsKubernetesManagedHosting() || environment.IsManagedAppEnvironment();
        }

        /// <summary>
        /// Gets a value indicating whether the application is running in Managed App environment.
        /// </summary>
        /// <param name="environment">The environment to verify.</param>
        /// <returns><see cref="true"/> if running in Managed App environment; otherwise, false.</returns>
        public static bool IsManagedAppEnvironment(this IEnvironment environment)
        {
            return !string.IsNullOrEmpty(environment.GetEnvironmentVariable(ManagedEnvironment));
        }

        /// <summary>
        /// Gets a value indicating whether the application is running in a Linux Consumption (dynamic)
        /// App Service environment.
        /// </summary>
        /// <param name="environment">The environment to verify.</param>
        /// <returns><see cref="true"/> if running in a Linux Consumption App Service app; otherwise, false.</returns>
        public static bool IsAnyLinuxConsumption(this IEnvironment environment)
        {
            return (environment.IsLinuxConsumptionOnAtlas() || environment.IsFlexConsumptionSku()) && !environment.IsManagedAppEnvironment();
        }

        public static bool IsLinuxConsumptionOnAtlas(this IEnvironment environment)
        {
            return !environment.IsAppService() &&
                   !string.IsNullOrEmpty(environment.GetEnvironmentVariable(ContainerName)) &&
                   string.IsNullOrEmpty(environment.GetEnvironmentVariable(LegionServiceHost));
        }

        private static bool IsLinuxConsumptionOnLegion(this IEnvironment environment)
        {
            return !environment.IsAppService() &&
                   (!string.IsNullOrEmpty(environment.GetEnvironmentVariable(ContainerName)) ||
                   !string.IsNullOrEmpty(environment.GetEnvironmentVariable(WebsitePodName))) &&
                   !string.IsNullOrEmpty(environment.GetEnvironmentVariable(LegionServiceHost));
        }

        /// <summary>
        /// Gets a value indicating whether the application is running in a Linux App Service
        /// environment (Dedicated Linux).
        /// </summary>
        /// <param name="environment">The environment to verify.</param>
        /// <returns><see cref="true"/> if running in a Linux Azure App Service; otherwise, false.</returns>
        public static bool IsLinuxAppService(this IEnvironment environment)
        {
            return environment.IsAppService() && !string.IsNullOrEmpty(environment.GetEnvironmentVariable(FunctionsLogsMountPath));
        }

        /// <summary>
        /// Gets a value indicating whether the application is running in an Azure Linux managed hosting environment
        /// (i.e. Linux Consumption or Linux Dedicated).
        /// </summary>
        /// <param name="environment">The environment to verify.</param>
        /// <returns><see cref="true"/> if running in a Linux Azure managed hosting environment; otherwise, false.</returns>
        public static bool IsLinuxAzureManagedHosting(this IEnvironment environment)
        {
            return environment.IsAnyLinuxConsumption() || environment.IsLinuxAppService();
        }

        /// <summary>
        /// Gets a value indicating whether the application is running in App Service
        /// (Windows Consumption, Windows Dedicated or Linux Dedicated).
        /// </summary>
        /// <param name="environment">The environment to verify.</param>
        /// <returns><see cref="true"/> if running in a Azure App Service; otherwise, false.</returns>
        public static bool IsAppService(this IEnvironment environment)
        {
            return !string.IsNullOrEmpty(environment.GetEnvironmentVariable(AzureWebsiteInstanceId));
        }

        /// <summary>
        /// Gets a value indicating whether the application is running in Kubernetes App Service environment(K8SE).
        /// </summary>
        /// <param name="environment">The environment to verify.</param>
        /// <returns><see cref="true"/> If running in a Kubernetes Azure App Service; otherwise, false.</returns>
        public static bool IsKubernetesManagedHosting(this IEnvironment environment)
        {
            return !string.IsNullOrEmpty(environment.GetEnvironmentVariable(KubernetesServiceHost))
            && !string.IsNullOrEmpty(environment.GetEnvironmentVariable(PodNamespace))
            && !environment.IsManagedAppEnvironment();
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
        /// Gets the computer name.
        /// </summary>
        public static string GetAntaresComputerName(this IEnvironment environment)
        {
            return environment.GetEnvironmentVariableOrDefault(AntaresComputerName, string.Empty);
        }

        /// <summary>
        /// Gets if runtime environment is logic apps.
        /// </summary>
        public static bool IsLogicApp(this IEnvironment environment)
        {
            string appKind = environment.GetEnvironmentVariable(AppKind)?.ToLower();
            return !string.IsNullOrEmpty(appKind) && appKind.Contains(ScriptConstants.WorkFlowAppKind);
        }

        /// <summary>
        /// Gets if runtime environment needs multi language.
        /// </summary>
        public static bool IsMultiLanguageRuntimeEnvironment(this IEnvironment environment)
        {
            if (!isMultiLanguageEnabled.HasValue)
            {
                isMultiLanguageEnabled = environment.IsLogicApp();
            }
            return isMultiLanguageEnabled.Value;
        }

        /// <summary>
        /// Gets the Antares version.
        /// </summary>
        public static string GetAntaresVersion(this IEnvironment environment)
        {
            if (environment.IsLinuxAzureManagedHosting())
            {
                return environment.GetEnvironmentVariableOrDefault(AntaresPlatformVersionLinux, string.Empty);
            }
            else
            {
                return environment.GetEnvironmentVariableOrDefault(AntaresPlatformVersionWindows, string.Empty);
            }
        }

        /// <summary>
        /// Gets the Instance id.
        /// </summary>
        public static string GetInstanceId(this IEnvironment environment)
        {
            if (environment.IsAnyLinuxConsumption())
            {
                return environment.GetEnvironmentVariableOrDefault(ContainerName, string.Empty);
            }
            else
            {
                return environment.GetEnvironmentVariableOrDefault(AzureWebsiteInstanceId, string.Empty);
            }
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

        /// <summary>
        /// Gets the name of the currently deployed site name.
        /// </summary>
        public static string GetRuntimeSiteName(this IEnvironment environment)
        {
            string runtimeSiteName = environment.GetEnvironmentVariable(AzureWebsiteRuntimeSiteName);
            return runtimeSiteName?.ToLowerInvariant();
        }

        /// <summary>
        /// Gets the name of the app's slot, if it exists.
        /// </summary>
        public static string GetSlotName(this IEnvironment environment)
        {
            string slotName = environment.GetEnvironmentVariable(AzureWebsiteSlotName);
            return slotName?.ToLowerInvariant();
        }

        /// <summary>
        /// Gets a value indicating whether it is safe to start specializing the host instance (e.g. file system is ready, etc.)
        /// </summary>
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
        {
            return string.Equals(environment.GetEnvironmentVariable(MountEnabled), "1")
                && !string.IsNullOrEmpty(environment.GetEnvironmentVariable(MeshInitURI));
        }

        public static bool IsMountDisabled(this IEnvironment environment)
        {
            return string.Equals(environment.GetEnvironmentVariable(MountEnabled), "0")
                || string.IsNullOrEmpty(environment.GetEnvironmentVariable(MeshInitURI));
        }

        public static CloudName GetCloudName(this IEnvironment environment)
        {
            var cloudName = environment.GetEnvironmentVariable(EnvironmentSettingNames.CloudName);
            if (Enum.TryParse(cloudName, true, out CloudName cloud))
            {
                return cloud;
            }

            return CloudName.Azure;
        }

        public static string GetStorageSuffix(this IEnvironment environment)
        {
            switch (GetCloudName(environment))
            {
                case CloudName.Azure:
                    return CloudConstants.AzureStorageSuffix;
                case CloudName.Blackforest:
                    return CloudConstants.BlackforestStorageSuffix;
                case CloudName.Fairfax:
                    return CloudConstants.FairfaxStorageSuffix;
                case CloudName.Mooncake:
                    return CloudConstants.MooncakeStorageSuffix;
                case CloudName.USNat:
                    return CloudConstants.USNatStorageSuffix;
                case CloudName.USSec:
                    return CloudConstants.USSecStorageSuffix;
                default:
                    return CloudConstants.AzureStorageSuffix;
            }
        }

        public static string GetVaultSuffix(this IEnvironment environment)
        {
            {
                switch (GetCloudName(environment))
                {
                    case CloudName.Azure:
                        return CloudConstants.AzureVaultSuffix;
                    case CloudName.Blackforest:
                        return CloudConstants.BlackforestVaultSuffix;
                    case CloudName.Fairfax:
                        return CloudConstants.FairfaxVaultSuffix;
                    case CloudName.Mooncake:
                        return CloudConstants.MooncakeVaultSuffix;
                    default:
                        return CloudConstants.AzureVaultSuffix;
                }
            }
        }

        public static string GetFunctionsWorkerRuntime(this IEnvironment environment)
        {
            return environment.GetEnvironmentVariableOrDefault(FunctionWorkerRuntime, string.Empty);
        }

        /// <summary>
        /// Gets a value indicating whether AzureFileShare should be mounted when specializing Linux Consumption workers.
        /// </summary>
        public static bool SupportsAzureFileShareMount(this IEnvironment environment)
        {
            return string.Equals(environment.GetFunctionsWorkerRuntime(), RpcWorkerConstants.PowerShellLanguageWorkerName,
                StringComparison.OrdinalIgnoreCase);
        }

        public static string GetHttpLeaderEndpoint(this IEnvironment environment)
        {
            return environment.GetEnvironmentVariableOrDefault(HttpLeaderEndpoint, string.Empty);
        }

        public static bool DrainOnApplicationStoppingEnabled(this IEnvironment environment)
        {
            return !string.IsNullOrEmpty(environment.GetEnvironmentVariable(KubernetesServiceHost)) ||
                (bool.TryParse(environment.GetEnvironmentVariable(DrainOnApplicationStopping), out bool v) && v);
        }

        public static bool IsWorkerDynamicConcurrencyEnabled(this IEnvironment environment)
        {
            if (bool.TryParse(environment.GetEnvironmentVariable(RpcWorkerConstants.FunctionsWorkerDynamicConcurrencyEnabled), out bool concurrencyEnabled))
            {
                return concurrencyEnabled && string.IsNullOrEmpty(environment.GetEnvironmentVariable(RpcWorkerConstants.FunctionsWorkerProcessCountSettingName));
            }
            return false;
        }

        public static HashSet<string> GetLanguageWorkerListToStartInPlaceholder(this IEnvironment environment)
        {
            string placeholderList = environment.GetEnvironmentVariableOrDefault(RpcWorkerConstants.FunctionWorkerPlaceholderModeListSettingName, string.Empty);
            var placeholderRuntimeSet = new HashSet<string>(placeholderList.Trim().Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()));
            string workerRuntime = environment.GetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName);
            if (!string.IsNullOrEmpty(workerRuntime))
            {
                placeholderRuntimeSet.Add(workerRuntime);
            }
            return placeholderRuntimeSet;
        }

        public static bool IsApplicationInsightsAgentEnabled(this IEnvironment environment)
        {
            // cache the value of the environment variable
            if (isApplicationInsightsAgentEnabled.HasValue)
            {
                return isApplicationInsightsAgentEnabled.Value;
            }
            else if (!environment.IsPlaceholderModeEnabled())
            {
                bool.TryParse(environment.GetEnvironmentVariable(AppInsightsAgent), out bool isEnabled);
                isApplicationInsightsAgentEnabled = isEnabled;
                return isApplicationInsightsAgentEnabled.Value;
            }
            return false;
        }

        /// <summary>
        /// Clears all cached static flags in <see cref="EnvironmentExtensions"/>.
        /// Currently we only use it to purge static initialisations in across tests.
        /// </summary>
        public static void ClearCache()
        {
            isMultiLanguageEnabled = null;
            isApplicationInsightsAgentEnabled = null;
        }

        /// <summary>
        /// Gets a value indicated in the variable FUNCTIONS_EXTENSION_VERSION.
        /// </summary>
        /// <returns>Value of FUNCTIONS_EXTENSION_VERSION variable.</returns>
        public static string GetFunctionsExtensionVersion(this IEnvironment environment)
        {
            return environment.GetEnvironmentVariableOrDefault(FunctionsExtensionVersion, string.Empty);
        }

        public static bool IsTargetBasedScalingEnabled(this IEnvironment environment)
        {
            return !string.Equals(environment.GetEnvironmentVariable(TargetBaseScalingEnabled), "0");
        }
    }
}