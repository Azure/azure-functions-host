﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Diagnostics.OpenTelemetry
{
    internal sealed class ResourceAttributeConstants
    {
        internal const string CloudProvider = "cloud.provider";
        internal const string CloudPlatform = "cloud.platform";
        internal const string CloudRegion = "cloud.region";
        internal const string CloudResourceId = "cloud.resource.id";
        internal const string ServiceInstanceId = "service.instance.id";
        internal const string ServiceName = "service.name";
        internal const string AzureFunctionsGroup = "azure.functions.group";

        internal const string AttributeTrigger = "faas.trigger";
        internal const string AttributeInvocationId = "faas.invocation_id";
        internal const string AttributeColdStart = "faas.coldstart";

        // Function name myazurefunctionapp/some-function-name
        internal const string AttributeName = "faas.name";
        internal const string AttributeVersion = "faas.version";
        internal const string AttributeInstance = "faas.instance";

        internal const string AzureCloudProviderValue = "azure";
        internal const string AzurePlatformValue = "azure_functions";
        internal const string AttributeSDKPrefix = "ai.sdk.prefix";
        internal const string AttributeProcessId = "process.pid";

        internal const string SDKPrefix = "azurefunctions";
        internal const string SiteNameEnvVar = "WEBSITE_SITE_NAME";
        internal const string RegionNameEnvVar = "REGION_NAME";
        internal const string ResourceGroupEnvVar = "WEBSITE_RESOURCE_GROUP";
        internal const string OwnerNameEnvVar = "WEBSITE_OWNER_NAME";

        internal static string ResolveTriggerType(string trigger)
        {
            switch (trigger)
            {
                case "httpTrigger":
                    return "http";
                default:
                    return trigger;
            }
        }
    }
}
