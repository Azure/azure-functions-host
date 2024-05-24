// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script
{
    public static class EnvironmentSettingNames
    {
        public const string AzureWebsiteName = "WEBSITE_SITE_NAME";
        public const string AzureWebsiteHostName = "WEBSITE_HOSTNAME";
        public const string AzureWebsiteSlotName = "WEBSITE_SLOT_NAME";
        public const string AzureWebsiteOwnerName = "WEBSITE_OWNER_NAME";
        public const string AzureWebsiteInstanceId = "WEBSITE_INSTANCE_ID";
        public const string AzureWebsiteSku = "WEBSITE_SKU";
        public const string AzureWebsiteZipDeployment = "WEBSITE_USE_ZIP";
        public const string RemoteDebuggingPort = "REMOTEDEBUGGINGPORT";
        public const string AzureWebsitePlaceholderMode = "WEBSITE_PLACEHOLDER_MODE";
        public const string AzureWebsiteHomePath = "HOME";
        public const string AzureWebJobsScriptRoot = "AzureWebJobsScriptRoot";
        public const string CompilationReleaseMode = "AzureWebJobsDotNetReleaseCompilation";
        public const string AzureWebJobsDisableHomepage = "AzureWebJobsDisableHomepage";
        public const string TypeScriptCompilerPath = "AzureWebJobs_TypeScriptPath";
        public const string AzureWebJobsExtensionsPath = "AzureWebJobs_ExtensionsPath";
        public const string AzureWebsiteAppCountersName = "WEBSITE_COUNTERS_APP";
        public const string AzureWebJobsSecretStorageType = "AzureWebJobsSecretStorageType";
        public const string AzureWebJobsSecretStorageSas = "AzureWebJobsSecretStorageSas";
        public const string AppInsightsInstrumentationKey = "APPINSIGHTS_INSTRUMENTATIONKEY";
        public const string ProxySiteExtensionEnabledKey = "ROUTING_EXTENSION_VERSION";
        public const string FunctionsExtensionVersion = "FUNCTIONS_EXTENSION_VERSION";
        public const string WebsiteAuthEncryptionKey = "WEBSITE_AUTH_ENCRYPTION_KEY";
        public const string SkipSslValidation = "SCM_SKIP_SSL_VALIDATION";
        public const string CoreToolsEnvironment = "FUNCTIONS_CORETOOLS_ENVIRONMENT";
        public const string AzureWebsiteArmCacheEnabled = "WEBSITE_FUNCTIONS_ARMCACHE_ENABLED";
        public const string TestDataCapEnabled = "WEBSITE_FUNCTIONS_TESTDATA_CAP_ENABLED";
        public const string AzureWebsiteRuntimeSiteName = "WEBSITE_DEPLOYMENT_ID";

        /// <summary>
        /// Environment variable dynamically set by the platform when it is safe to
        /// start specializing the host instance (e.g. file system is ready, etc.)
        /// </summary>
        public const string AzureWebsiteContainerReady = "WEBSITE_CONTAINER_READY";

        /// <summary>
        /// Environment variable dynamically set by the platform when configuration has been
        /// completely initialized (e.g. EnvSettings module has ran) and it is safe to read
        /// configuration values.
        /// </summary>
        public const string AzureWebsiteConfigurationReady = "WEBSITE_CONFIGURATION_READY";
    }
}
