// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.
using System;

using Microsoft.Azure.WebJobs.Script.Description;

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

        internal static bool TryResolveHttpTriggerRoute(BindingMetadata? bindingMetadata, out string httpRoute)
        {
            if (bindingMetadata is not null && bindingMetadata?.Raw is not null && bindingMetadata.Raw.TryGetValue("route", StringComparison.OrdinalIgnoreCase, out var value))
            {
                httpRoute = value.ToString();
                return true;
            }

            httpRoute = null;
            return false;
        }
    }
}
