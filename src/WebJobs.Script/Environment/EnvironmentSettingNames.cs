﻿// Copyright (c) .NET Foundation. All rights reserved.
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
        public const string AzureWebsiteAltZipDeployment = "WEBSITE_RUN_FROM_ZIP";
        public const string RemoteDebuggingPort = "REMOTEDEBUGGINGPORT";
        public const string AzureWebsitePlaceholderMode = "WEBSITE_PLACEHOLDER_MODE";
        public const string AzureWebsiteHomePath = "HOME";
        public const string AzureWebJobsScriptRoot = "AzureWebJobsScriptRoot";
        public const string AzureWebJobsEnvironment = "AzureWebJobsEnv";
        public const string CompilationReleaseMode = "AzureWebJobsDotNetReleaseCompilation";
        public const string AzureWebJobsDisableHomepage = "AzureWebJobsDisableHomepage";
        public const string TypeScriptCompilerPath = "AzureWebJobs_TypeScriptPath";
        public const string AzureWebsiteAppCountersName = "WEBSITE_COUNTERS_APP";
        public const string AzureWebJobsSecretStorageType = "AzureWebJobsSecretStorageType";
        public const string AppInsightsInstrumentationKey = "APPINSIGHTS_INSTRUMENTATIONKEY";
        public const string ProxySiteExtensionEnabledKey = "ROUTING_EXTENSION_VERSION";
        public const string FunctionsExtensionVersion = "FUNCTIONS_EXTENSION_VERSION";
        public const string ContainerName = "CONTAINER_NAME";
        public const string WebSiteAuthEncryptionKey = "WEBSITE_AUTH_ENCRYPTION_KEY";
        public const string ContainerEncryptionKey = "CONTAINER_ENCRYPTION_KEY";
        public const string ConsoleLoggingDisabled = "CONSOLE_LOGGING_DISABLED";
        public const string SkipSslValidation = "SCM_SKIP_SSL_VALIDATION";

        /// <summary>
        /// Environment variable dynamically set by the platform when it is safe to
        /// start specializing the host instance (e.g. file system is ready, etc.)
        /// </summary>
        public const string AzureWebsiteContainerReady = "WEBSITE_CONTAINER_READY";

        public const string ContainerStartContext = "CONTAINER_START_CONTEXT";
        public const string ContainerStartContextSasUri = "CONTAINER_START_CONTEXT_SAS_URI";
    }
}
