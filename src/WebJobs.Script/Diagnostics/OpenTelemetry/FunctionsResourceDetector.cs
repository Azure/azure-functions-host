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
                    new(ResourceSemanticConventions.AISDKPrefix, $"{OpenTelemetryConstants.SDKPrefix}:{AssemblyVersion}"),
                    new(ResourceSemanticConventions.ProcessId, ProcessId)
                };

                string? azureWebsiteName = Environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName);
                string? resourceAttributes = Environment.GetEnvironmentVariable(ResourceSemanticConventions.ResourceAttributeEnvVar);

                // Priority: OTEL_SERVICE_NAME > OTEL_RESOURCE_ATTRIBUTES[service.name] > AzureWebsiteName > AssemblyName
                if (!IsServiceNameConfigured(resourceAttributes))
                {
                    attributes.Add(new(ResourceSemanticConventions.ServiceName, azureWebsiteName ?? typeof(ScriptHost).Assembly.GetName().Name ?? "unknown"));
                }

                // Priority: OTEL_RESOURCE_ATTRIBUTES[service.version] > AssemblyVersion
                // OTel decided to not have OTEL_SERVICE_VERSION, so we only check OTEL_RESOURCE_ATTRIBUTES
                // https://github.com/open-telemetry/semantic-conventions/issues/2669
                if (!IsResourceAttributeConfigured(ResourceSemanticConventions.ServiceVersion, resourceAttributes))
                {
                    attributes.Add(new(ResourceSemanticConventions.ServiceVersion, AssemblyVersion));
                }

                // Only add Azure-specific attributes if WEBSITE_SITE_NAME is defined
                if (!string.IsNullOrEmpty(azureWebsiteName))
                {
                    attributes.AddRange(
                    [
                        new(ResourceSemanticConventions.CloudProvider, OpenTelemetryConstants.AzureCloudProviderValue),
                        new(ResourceSemanticConventions.CloudPlatform, OpenTelemetryConstants.AzurePlatformValue)
                    ]);

                    if (Environment.GetEnvironmentVariable(EnvironmentSettingNames.RegionName) is { Length: > 0 } region)
                    {
                        attributes.Add(new(ResourceSemanticConventions.CloudRegion, region));
                    }

                    if (GetAzureResourceUri(azureWebsiteName) is { } uri)
                    {
                        attributes.Add(new(ResourceSemanticConventions.CloudResourceId, uri));
                    }

                    if (Environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSlotName) is { Length: > 0 } slot)
                    {
                        attributes.Add(new(ResourceSemanticConventions.DeploymentEnvironmentName, slot));
                    }

                    if (Environment.GetEnvironmentVariable(EnvironmentSettingNames.FunctionsSiteUpdateId) is { Length: > 0 } appVersion)
                    {
                        attributes.Add(new(ResourceSemanticConventions.SiteUpdateId, appVersion));
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

        private static bool IsServiceNameConfigured(string? resourceAttributes)
        {
            // Check OTEL_SERVICE_NAME first
            if (Environment.GetEnvironmentVariable(ResourceSemanticConventions.ServiceNameEnvVar) is { Length: > 0 })
            {
                return true;
            }

            // Fall back to checking OTEL_RESOURCE_ATTRIBUTES
            return IsResourceAttributeConfigured(ResourceSemanticConventions.ServiceName, resourceAttributes);
        }

        private static bool IsResourceAttributeConfigured(string key, string? resourceAttributes)
        {
            if (resourceAttributes is not { Length: > 2 })
            {
                return false;
            }

            // TODO: Replace manual parsing with MemoryExtensions.Split when we upgrade to .NET 10
            var remaining = resourceAttributes.AsSpan();

            while (remaining.Length > 0)
            {
                var commaIndex = remaining.IndexOf(',');
                var segment = commaIndex >= 0
                    ? remaining[..commaIndex]
                    : remaining;

                var trimmed = segment.Trim();
                var equalsIndex = trimmed.IndexOf('=');

                if (equalsIndex > 0)
                {
                    var attributeKey = trimmed[..equalsIndex];
                    if (attributeKey.Equals(key, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }

                if (commaIndex < 0)
                {
                    break;
                }

                remaining = remaining[(commaIndex + 1)..];
            }

            return false;
        }
    }
}
