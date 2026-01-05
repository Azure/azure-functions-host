// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using OpenTelemetry.Resources;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics.OpenTelemetry
{
    internal sealed class FunctionsResourceDetector : IResourceDetector
    {
        private static readonly string AssemblyVersion = typeof(ScriptHost).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";
        private static readonly int ProcessId = Process.GetCurrentProcess().Id;

        public Resource Detect()
        {
            try
            {
                var attributes = new List<KeyValuePair<string, object>>(capacity: 10)
                {
                    new(ResourceSemConventions.AISDKPrefix, $"{OpenTelemetryConstants.SDKPrefix}:{AssemblyVersion}"),
                    new(ResourceSemConventions.ProcessId, ProcessId)
                };

                string? azureWebsiteName = Environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName);

                // Priority: OTEL_SERVICE_NAME > OTEL_RESOURCE_ATTRIBUTES[service.name] > AzureWebsiteName > AssemblyName
                // Only add service.name if not already configured
                if (!IsServiceAttributeConfigured(ResourceSemConventions.ServiceName,
                                               ResourceSemConventions.ServiceNameEnvVar))
                {
                    attributes.Add(new(ResourceSemConventions.ServiceName, azureWebsiteName ?? typeof(ScriptHost).Assembly.GetName().Name ?? "unknown"));
                }

                // Priority: OTEL_SERVICE_Version > OTEL_RESOURCE_ATTRIBUTES[service.version] > AssemblyVersion
                // Only add service.version if not already configured
                if (!IsServiceAttributeConfigured(ResourceSemConventions.ServiceVersion,
                                               ResourceSemConventions.ServiceVersionEnvVar))
                {
                    attributes.Add(new(ResourceSemConventions.ServiceVersion, AssemblyVersion));
                }

                // Only add Azure-specific attributes if WEBSITE_SITE_NAME is defined
                if (!string.IsNullOrEmpty(azureWebsiteName))
                {
                    attributes.AddRange(
                    [
                        new(ResourceSemConventions.CloudProvider, OpenTelemetryConstants.AzureCloudProviderValue),
                        new(ResourceSemConventions.CloudPlatform, OpenTelemetryConstants.AzurePlatformValue)
                    ]);

                    if (Environment.GetEnvironmentVariable(EnvironmentSettingNames.RegionName) is { Length: > 0 } region)
                    {
                        attributes.Add(new(ResourceSemConventions.CloudRegion, region));
                    }

                    if (GetAzureResourceUri(azureWebsiteName) is { } uri)
                    {
                        attributes.Add(new(ResourceSemConventions.CloudResourceId, uri));
                    }

                    if (Environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSlotName) is { Length: > 0 } slot)
                    {
                        attributes.Add(new(ResourceSemConventions.DeploymentEnvironmentName, slot));
                    }

                    if (Environment.GetEnvironmentVariable(EnvironmentSettingNames.FunctionAppVersion) is { Length: > 0 } appVersion)
                    {
                        attributes.Add(new(ResourceSemConventions.SiteUpdateId, appVersion));
                    }
                }

                return new Resource(attributes);
            }
            catch
            {
                // return empty resource.
                return Resource.Empty;
            }
        }

        private static string? GetAzureResourceUri(string siteName)
        {
            var resourceGroup = Environment.GetEnvironmentVariable(EnvironmentSettingNames.ResourceGroup);
            var owner = Environment.GetEnvironmentVariable(EnvironmentSettingNames.WebsiteOwnerName);

            if (string.IsNullOrEmpty(resourceGroup) || string.IsNullOrEmpty(owner))
            {
                return null;
            }

            // owner format: "{subscriptionId}+{something}"
            var span = owner.AsSpan();
            var plusIndex = span.IndexOf('+');

            var subscriptionId = plusIndex > 0
                ? span[..plusIndex].ToString()
                : owner;

            return $"/subscriptions/{subscriptionId}/resourceGroups/{resourceGroup}/providers/Microsoft.Web/sites/{siteName}";
        }

        private static bool IsServiceAttributeConfigured(string key, string envVar)
        {
            if (Environment.GetEnvironmentVariable(envVar) is { Length: > 0 })
            {
                return true;
            }

            if (Environment.GetEnvironmentVariable(ResourceSemConventions.ResourceAttributeEnvVar) is not { Length: > 0 } raw)
            {
                return false;
            }

            foreach (var segment in raw.Split(','))
            {
                var trimmed = segment.AsSpan().Trim();

                if (trimmed.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
