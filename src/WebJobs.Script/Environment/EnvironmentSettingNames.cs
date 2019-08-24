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
        public const string RemoteDebuggingPort = "REMOTEDEBUGGINGPORT";
        public const string AzureWebsitePlaceholderMode = "WEBSITE_PLACEHOLDER_MODE";
        public const string AzureWebsiteHomePath = "HOME";
        public const string AzureWebJobsScriptRoot = "AzureWebJobsScriptRoot";
        public const string CompilationReleaseMode = "AzureWebJobsDotNetReleaseCompilation";
        public const string AzureWebJobsDisableHomepage = "AzureWebJobsDisableHomepage";
        public const string TypeScriptCompilerPath = "AzureWebJobs_TypeScriptPath";
        public const string AzureWebsiteAppCountersName = "WEBSITE_COUNTERS_APP";
        public const string AzureWebJobsSecretStorageType = "AzureWebJobsSecretStorageType";
        public const string AzureWebJobsSecretStorageSas = "AzureWebJobsSecretStorageSas";
        public const string AppInsightsInstrumentationKey = "APPINSIGHTS_INSTRUMENTATIONKEY";
        public const string AppInsightsQuickPulseAuthApiKey = "APPINSIGHTS_QUICKPULSEAUTHAPIKEY";
        public const string FunctionsExtensionVersion = "FUNCTIONS_EXTENSION_VERSION";
        public const string ContainerName = "CONTAINER_NAME";
        public const string WebSiteHomeStampName = "WEBSITE_HOME_STAMPNAME";
        public const string WebSiteStampDeploymentId = "WEBSITE_STAMP_DEPLOYMENT_ID";
        public const string WebSiteAuthEncryptionKey = "WEBSITE_AUTH_ENCRYPTION_KEY";
        public const string ContainerEncryptionKey = "CONTAINER_ENCRYPTION_KEY";
        public const string ConsoleLoggingDisabled = "CONSOLE_LOGGING_DISABLED";
        public const string SkipSslValidation = "SCM_SKIP_SSL_VALIDATION";
        public const string EnvironmentNameKey = "AZURE_FUNCTIONS_ENVIRONMENT";
        public const string EasyAuthEnabled = "WEBSITE_AUTH_ENABLED";
        public const string AzureWebJobsSecretStorageKeyVaultName = "AzureWebJobsSecretStorageKeyVaultName";
        public const string AzureWebJobsSecretStorageKeyVaultConnectionString = "AzureWebJobsSecretStorageKeyVaultConnectionString";
        public const string AzureWebsiteArmCacheEnabled = "WEBSITE_FUNCTIONS_ARMCACHE_ENABLED";
        public const string MountEnabled = "WEBSITE_MOUNT_ENABLED";
        public const string MsiEndpoint = "MSI_ENDPOINT";
        public const string MsiSecret = "MSI_SECRET";
        public const string DotnetSkipFirstTimeExperience = "DOTNET_SKIP_FIRST_TIME_EXPERIENCE";
        public const string AzureFilesConnectionString = "WEBSITE_CONTENTAZUREFILECONNECTIONSTRING";
        public const string AzureFilesContentShare = "WEBSITE_CONTENTSHARE";

        /// <summary>
        /// Environment variable dynamically set by the platform when it is safe to
        /// start specializing the host instance (e.g. file system is ready, etc.)
        /// </summary>
        public const string AzureWebsiteContainerReady = "WEBSITE_CONTAINER_READY";

        public const string ContainerStartContext = "CONTAINER_START_CONTEXT";
        public const string ContainerStartContextSasUri = "CONTAINER_START_CONTEXT_SAS_URI";
        public const string FunctionsLogsMountPath = "FUNCTIONS_LOGS_MOUNT_PATH";

        // unfortunately there are 3 versions of this setting that have to be supported
        // due to renames
        public const string AzureWebsiteZipDeployment = "WEBSITE_USE_ZIP";
        public const string AzureWebsiteAltZipDeployment = "WEBSITE_RUN_FROM_ZIP";
        public const string AzureWebsiteRunFromPackage = "WEBSITE_RUN_FROM_PACKAGE";
        public const string RegionName = "REGION_NAME";

        // handling server side builds
        public const string ScmRunFromPackage = "SCM_RUN_FROM_PACKAGE";

        public const string LinuxAzureAppServiceStorage = "WEBSITES_ENABLE_APP_SERVICE_STORAGE";
        public const string CoreToolsEnvironment = "FUNCTIONS_CORETOOLS_ENVIRONMENT";
        public const string RunningInContainer = "DOTNET_RUNNING_IN_CONTAINER";

        public const string ExtensionBundleSourceUri = "FUNCTIONS_EXTENSIONBUNDLE_SOURCE_URI";

        public const string LinuxNodeIpAddress = "Fabric_NodeIPOrFQDN";
        public const string AzureWebJobsKubernetesSecretName = "AzureWebJobsKubernetesSecretName";

        public const string KubernetesServiceHost = "KUBERNETES_SERVICE_HOST";
        public const string KubernetesServiceHttpsPort = "KUBERNETES_SERVICE_PORT_HTTPS";
        public const string FunctionsLogPath = "FUNCTIONS_LOG_PATH";
        public const string FunctionsSecretsPath = "FUNCTIONS_SECRETS_PATH";
        public const string FunctionsTestDataPath = "FUNCTIONS_TEST_DATA_PATH";
        public const string MeshInitURI = "MESH_INIT_URI";
    }
}
