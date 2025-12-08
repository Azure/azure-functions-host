// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using OpenTelemetry.Resources;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Xml.Linq;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics.OpenTelemetry
{
    internal sealed class FunctionsResourceDetector : IResourceDetector
    {
        private static readonly string _assemblyVersion = typeof(ScriptHost).Assembly.GetName().Version.ToString();
        private static readonly int _processId = Process.GetCurrentProcess().Id;

        public Resource Detect()
        {
            List<KeyValuePair<string, object>> attributeList = new(9);
            try
            {
                // Determine service name with override hierarchy: OTEL_SERVICE_NAME > AzureWebsiteName > AssemblyName
                string serviceName = string.Empty; // GetServiceName();

                // Determine service version with override hierarchy: OTEL_SERVICE_VERSION > AssemblyVersion
                string serviceVersion = GetServiceVersion();

                // Add version and SDK prefix attributes
                attributeList.Add(new KeyValuePair<string, object>(ResourceSemanticConventions.ServiceVersion, serviceVersion));
                attributeList.Add(new KeyValuePair<string, object>(ResourceSemanticConventions.AISDKPrefix, $"{OpenTelemetryConstants.SDKPrefix}:{serviceVersion}"));
                attributeList.Add(new KeyValuePair<string, object>(ResourceSemanticConventions.ProcessId, _processId));
                attributeList.Add(new KeyValuePair<string, object>(ResourceSemanticConventions.ServiceName, serviceName));

                // Only add Azure-specific attributes if WEBSITE_SITE_NAME is defined
                string azureWebsiteName = Environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName);

                if (!string.IsNullOrEmpty(azureWebsiteName))
                {
                    attributeList.Add(new KeyValuePair<string, object>(ResourceSemanticConventions.CloudProvider, OpenTelemetryConstants.AzureCloudProviderValue));
                    attributeList.Add(new KeyValuePair<string, object>(ResourceSemanticConventions.CloudPlatform, OpenTelemetryConstants.AzurePlatformValue));

                    string region = Environment.GetEnvironmentVariable(EnvironmentSettingNames.RegionName);
                    if (!string.IsNullOrEmpty(region))
                    {
                        attributeList.Add(new KeyValuePair<string, object>(ResourceSemanticConventions.CloudRegion, region));
                    }

                    var azureResourceUri = GetAzureResourceURI(azureWebsiteName);
                    if (azureResourceUri != null)
                    {
                        attributeList.Add(new KeyValuePair<string, object>(ResourceSemanticConventions.CloudResourceId, azureResourceUri));
                    }

                    string slotName = Environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSlotName);
                    if (!string.IsNullOrEmpty(slotName))
                    {
                        attributeList.Add(new KeyValuePair<string, object>(ResourceSemanticConventions.DeploymentEnvironmentName, slotName));
                    }
                }
            }
            catch
            {
                // return empty resource.
                return Resource.Empty;
            }

            return new Resource(attributeList);
        }

        private static string GetAzureResourceURI(string websiteSiteName)
        {
            string websiteResourceGroup = Environment.GetEnvironmentVariable(EnvironmentSettingNames.ResourceGroup);
            string websiteOwnerName = Environment.GetEnvironmentVariable(EnvironmentSettingNames.WebsiteOwnerName) ?? string.Empty;
            int idx = websiteOwnerName.IndexOf('+', StringComparison.Ordinal);
            string subscriptionId = idx > 0 ? websiteOwnerName.Substring(0, idx) : websiteOwnerName;

            if (string.IsNullOrEmpty(websiteResourceGroup) || string.IsNullOrEmpty(subscriptionId))
            {
                return null;
            }

            return $"/subscriptions/{subscriptionId}/resourceGroups/{websiteResourceGroup}/providers/Microsoft.Web/sites/{websiteSiteName}";
        }

        private static string GetServiceName()
        {
            // Priority: OTEL_SERVICE_NAME > AzureWebsiteName > AssemblyName
            string serviceName = Environment.GetEnvironmentVariable(ResourceSemanticConventions.ServiceNameEnvVar);
            if (!string.IsNullOrEmpty(serviceName))
            {
                return serviceName;
            }

            serviceName = Environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName);
            if (!string.IsNullOrEmpty(serviceName))
            {
                return serviceName;
            }

            // Fallback to assembly name
            return typeof(ScriptHost).Assembly.GetName().Name;
        }

        private static string GetServiceVersion()
        {
            // Priority: OTEL_SERVICE_VERSION > AssemblyVersion
            string version = Environment.GetEnvironmentVariable(ResourceSemanticConventions.ServiceVersionEnvVar);
            return !string.IsNullOrEmpty(version) ? version : _assemblyVersion;
        }
    }
}
