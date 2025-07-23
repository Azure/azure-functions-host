// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Diagnostics.OpenTelemetry
{
    internal static class OpenTelemetryConstants
    {
        internal const string AzureCloudProviderValue = "azure";
        internal const string AzurePlatformValue = "azure_functions";
        internal const string SDKPrefix = "azurefunctions";
        internal const string SiteNameEnvVar = "WEBSITE_SITE_NAME";
        internal const string RegionNameEnvVar = "REGION_NAME";
        internal const string ResourceGroupEnvVar = "WEBSITE_RESOURCE_GROUP";
        internal const string OwnerNameEnvVar = "WEBSITE_OWNER_NAME";
        internal const string AzureFunctionsGroup = "azure.functions.group";
        internal const string HttpTriggerType = "http";
    }
}
