// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Diagnostics.OpenTelemetry;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics.OpenTelemetry
{
    public class FunctionsResourceDetectorTests
    {
        private readonly FunctionsResourceDetector _detector = new();

        [Fact]
        public void Detect_ReturnsResource_WithServiceNameFromAssembly_WhenNoEnvironmentVariablesSet()
        {
            using var envVariables = SetupCleanEnvironment();

            var resource = _detector.Detect();

            var attributes = resource.Attributes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            Assert.True(attributes.ContainsKey(ResourceSemConventions.ServiceName));
            Assert.True(attributes.ContainsKey(ResourceSemConventions.ServiceVersion));
            Assert.True(attributes.ContainsKey(ResourceSemConventions.ProcessId));
            Assert.True(attributes.ContainsKey(ResourceSemConventions.AISDKPrefix));
        }

        [Fact]
        public void Detect_DoesNotIncludeServiceName_WhenOtelServiceNameEnvVarIsSet()
        {
            using var envVariables = new TestScopedEnvironmentVariable(ResourceSemConventions.ServiceNameEnvVar, "CustomServiceName");

            var resource = _detector.Detect();

            var attributes = resource.Attributes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            Assert.False(attributes.ContainsKey(ResourceSemConventions.ServiceName));
        }

        [Fact]
        public void Detect_DoesNotIncludeServiceName_WhenServiceNameInResourceAttributes()
        {
            using var envVariables = new TestScopedEnvironmentVariable(ResourceSemConventions.ResourceAttributeEnvVar, "service.name=CustomService,other=value");

            var resource = _detector.Detect();

            var attributes = resource.Attributes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            Assert.False(attributes.ContainsKey(ResourceSemConventions.ServiceName));
        }

        [Fact]
        public void Detect_DoesNotIncludeServiceName_WhenServiceNameInResourceAttributes_CaseInsensitive()
        {
            using var envVariables = new TestScopedEnvironmentVariable(ResourceSemConventions.ResourceAttributeEnvVar, "Service.Name=CustomService,other=value");

            var resource = _detector.Detect();

            var attributes = resource.Attributes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            Assert.False(attributes.ContainsKey(ResourceSemConventions.ServiceName));
        }

        [Fact]
        public void Detect_ReturnsServiceName_FromAzureWebsiteName_WhenOtelServiceNameNotSet()
        {
            using var envVariables = new TestScopedEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName, "MyFunctionApp");

            var resource = _detector.Detect();

            var serviceName = resource.Attributes.FirstOrDefault(a => string.Equals(a.Key, ResourceSemConventions.ServiceName, StringComparison.Ordinal)).Value;

            Assert.Equal("MyFunctionApp", serviceName);
        }

        [Fact]
        public void Detect_OtelServiceName_TakesPrecedence_OverAzureWebsiteName()
        {
            using var envVariables = new TestScopedEnvironmentVariable(new Dictionary<string, string>
            {
                { ResourceSemConventions.ServiceNameEnvVar, "OtelServiceName" },
                { EnvironmentSettingNames.AzureWebsiteName, "AzureWebsiteName" }
            });

            var resource = _detector.Detect();

            var attributes = resource.Attributes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            Assert.False(attributes.ContainsKey(ResourceSemConventions.ServiceName));
        }

        [Fact]
        public void Detect_DoesNotIncludeServiceVersion_WhenOtelServiceVersionEnvVarIsSet()
        {
            using var envVariables = new TestScopedEnvironmentVariable(ResourceSemConventions.ServiceVersionEnvVar, "1.2.3");

            var resource = _detector.Detect();

            var attributes = resource.Attributes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            Assert.False(attributes.ContainsKey(ResourceSemConventions.ServiceVersion));
        }

        [Fact]
        public void Detect_DoesNotIncludeServiceVersion_WhenServiceVersionInResourceAttributes()
        {
            using var envVariables = new TestScopedEnvironmentVariable(ResourceSemConventions.ResourceAttributeEnvVar, "service.version=1.0.0,other=value");

            var resource = _detector.Detect();

            var attributes = resource.Attributes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            Assert.False(attributes.ContainsKey(ResourceSemConventions.ServiceVersion));
        }

        [Fact]
        public void Detect_IncludesAzureCloudAttributes_WhenAzureWebsiteNameIsSet()
        {
            using var envVariables = new TestScopedEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName, "MyFunctionApp");

            var resource = _detector.Detect();

            var attributes = resource.Attributes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            Assert.Equal(OpenTelemetryConstants.AzureCloudProviderValue, attributes[ResourceSemConventions.CloudProvider]);
            Assert.Equal(OpenTelemetryConstants.AzurePlatformValue, attributes[ResourceSemConventions.CloudPlatform]);
        }

        [Fact]
        public void Detect_DoesNotIncludeAzureCloudAttributes_WhenAzureWebsiteNameIsNotSet()
        {
            using var envVariables = SetupCleanEnvironment();

            var resource = _detector.Detect();

            var attributes = resource.Attributes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            Assert.False(attributes.ContainsKey(ResourceSemConventions.CloudProvider));
            Assert.False(attributes.ContainsKey(ResourceSemConventions.CloudPlatform));
        }

        [Fact]
        public void Detect_IncludesCloudRegion_WhenRegionNameIsSet()
        {
            using var envVariables = new TestScopedEnvironmentVariable(new Dictionary<string, string>
            {
                { EnvironmentSettingNames.AzureWebsiteName, "MyFunctionApp" },
                { EnvironmentSettingNames.RegionName, "eastus" }
            });

            var resource = _detector.Detect();

            var region = resource.Attributes.FirstOrDefault(a => string.Equals(a.Key, ResourceSemConventions.CloudRegion, StringComparison.Ordinal)).Value;

            Assert.Equal("eastus", region);
        }

        [Fact]
        public void Detect_DoesNotIncludeCloudRegion_WhenRegionNameIsNotSet()
        {
            using var envVariables = new TestScopedEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName, "MyFunctionApp");

            var resource = _detector.Detect();

            var attributes = resource.Attributes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            Assert.False(attributes.ContainsKey(ResourceSemConventions.CloudRegion));
        }

        [Fact]
        public void Detect_IncludesCloudResourceId_WhenResourceGroupAndSubscriptionAreSet()
        {
            using var envVariables = new TestScopedEnvironmentVariable(new Dictionary<string, string>
            {
                { EnvironmentSettingNames.AzureWebsiteName, "MyFunctionApp" },
                { EnvironmentSettingNames.ResourceGroup, "my-rg" },
                { EnvironmentSettingNames.WebsiteOwnerName, "sub-id-123+my-rg-westeurope" }
            });

            var resource = _detector.Detect();

            var resourceId = resource.Attributes.FirstOrDefault(a => string.Equals(a.Key, ResourceSemConventions.CloudResourceId, StringComparison.Ordinal)).Value;

            Assert.Equal("/subscriptions/sub-id-123/resourceGroups/my-rg/providers/Microsoft.Web/sites/MyFunctionApp", resourceId);
        }

        [Fact]
        public void Detect_DoesNotIncludeCloudResourceId_WhenResourceGroupIsNotSet()
        {
            using var envVariables = new TestScopedEnvironmentVariable(new Dictionary<string, string>
            {
                { EnvironmentSettingNames.AzureWebsiteName, "MyFunctionApp" },
                { EnvironmentSettingNames.WebsiteOwnerName, "sub-id-123+my-rg-westeurope" }
            });

            var resource = _detector.Detect();

            var attributes = resource.Attributes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            Assert.False(attributes.ContainsKey(ResourceSemConventions.CloudResourceId));
        }

        [Fact]
        public void Detect_DoesNotIncludeCloudResourceId_WhenSubscriptionIdCannotBeParsed()
        {
            using var envVariables = new TestScopedEnvironmentVariable(new Dictionary<string, string>
            {
                { EnvironmentSettingNames.AzureWebsiteName, "MyFunctionApp" },
                { EnvironmentSettingNames.ResourceGroup, "my-rg" },
                { EnvironmentSettingNames.WebsiteOwnerName, "" }
            });

            var resource = _detector.Detect();

            var attributes = resource.Attributes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            Assert.False(attributes.ContainsKey(ResourceSemConventions.CloudResourceId));
        }

        [Fact]
        public void Detect_IncludesDeploymentEnvironmentName_WhenSlotNameIsSet()
        {
            using var envVariables = new TestScopedEnvironmentVariable(new Dictionary<string, string>
            {
                { EnvironmentSettingNames.AzureWebsiteName, "MyFunctionApp" },
                { EnvironmentSettingNames.AzureWebsiteSlotName, "staging" }
            });

            var resource = _detector.Detect();

            var slotName = resource.Attributes.FirstOrDefault(a => string.Equals(a.Key, ResourceSemConventions.DeploymentEnvironmentName, StringComparison.Ordinal)).Value;

            Assert.Equal("staging", slotName);
        }

        [Fact]
        public void Detect_DoesNotIncludeDeploymentEnvironmentName_WhenSlotNameIsNotSet()
        {
            using var envVariables = new TestScopedEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName, "MyFunctionApp");

            var resource = _detector.Detect();

            var attributes = resource.Attributes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            Assert.False(attributes.ContainsKey(ResourceSemConventions.DeploymentEnvironmentName));
        }

        [Fact]
        public void Detect_IncludesAppDeploymentId_WhenFunctionAppVersionIsSet()
        {
            using var envVariables = new TestScopedEnvironmentVariable(new Dictionary<string, string>
            {
                { EnvironmentSettingNames.AzureWebsiteName, "MyFunctionApp" },
                { EnvironmentSettingNames.FunctionAppVersion, "abc123" }
            });

            var resource = _detector.Detect();

            var appVersion = resource.Attributes.FirstOrDefault(a => string.Equals(a.Key, ResourceSemConventions.SiteUpdateId, StringComparison.Ordinal)).Value;

            Assert.Equal("abc123", appVersion);
        }

        [Fact]
        public void Detect_DoesNotIncludeAppDeploymentId_WhenFunctionAppVersionIsNotSet()
        {
            using var envVariables = new TestScopedEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName, "MyFunctionApp");

            var resource = _detector.Detect();

            var attributes = resource.Attributes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            Assert.False(attributes.ContainsKey(ResourceSemConventions.SiteUpdateId));
        }

        [Fact]
        public void Detect_IncludesProcessId()
        {
            using var envVariables = SetupCleanEnvironment();

            var resource = _detector.Detect();

            var attributes = resource.Attributes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            Assert.True(attributes.TryGetValue(ResourceSemConventions.ProcessId, out var processId));
            Assert.IsType<long>(processId);
        }

        [Fact]
        public void Detect_IncludesAISDKPrefix_WithCorrectFormat()
        {
            using var envVariables = SetupCleanEnvironment();

            var resource = _detector.Detect();

            var sdkPrefix = resource.Attributes.FirstOrDefault(a => string.Equals(a.Key, ResourceSemConventions.AISDKPrefix, StringComparison.Ordinal)).Value?.ToString();

            Assert.NotNull(sdkPrefix);
            Assert.StartsWith($"{OpenTelemetryConstants.SDKPrefix}:", sdkPrefix, StringComparison.Ordinal);
        }

        [Fact]
        public void Detect_ReturnsAllAzureAttributes_WhenAllEnvironmentVariablesAreSet()
        {
            using var envVariables = new TestScopedEnvironmentVariable(new Dictionary<string, string>
            {
                { EnvironmentSettingNames.AzureWebsiteName, "MyFunctionApp" },
                { EnvironmentSettingNames.RegionName, "eastus" },
                { EnvironmentSettingNames.ResourceGroup, "my-rg" },
                { EnvironmentSettingNames.WebsiteOwnerName, "sub-id+my-rg-region" },
                { EnvironmentSettingNames.AzureWebsiteSlotName, "production" },
                { EnvironmentSettingNames.FunctionAppVersion, "v1.0.0" }
            });

            var resource = _detector.Detect();

            var attributes = resource.Attributes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            Assert.True(attributes.ContainsKey(ResourceSemConventions.ServiceName));
            Assert.True(attributes.ContainsKey(ResourceSemConventions.ServiceVersion));
            Assert.True(attributes.ContainsKey(ResourceSemConventions.ProcessId));
            Assert.True(attributes.ContainsKey(ResourceSemConventions.AISDKPrefix));
            Assert.True(attributes.ContainsKey(ResourceSemConventions.CloudProvider));
            Assert.True(attributes.ContainsKey(ResourceSemConventions.CloudPlatform));
            Assert.True(attributes.ContainsKey(ResourceSemConventions.CloudRegion));
            Assert.True(attributes.ContainsKey(ResourceSemConventions.CloudResourceId));
            Assert.True(attributes.ContainsKey(ResourceSemConventions.DeploymentEnvironmentName));
            Assert.True(attributes.ContainsKey(ResourceSemConventions.SiteUpdateId));
        }

        private static IDisposable SetupCleanEnvironment()
        {
            return new TestScopedEnvironmentVariable(new Dictionary<string, string>
            {
                { ResourceSemConventions.ServiceNameEnvVar, null },
                { ResourceSemConventions.ServiceVersionEnvVar, null },
                { ResourceSemConventions.ResourceAttributeEnvVar, null },
                { EnvironmentSettingNames.AzureWebsiteName, null },
                { EnvironmentSettingNames.RegionName, null },
                { EnvironmentSettingNames.ResourceGroup, null },
                { EnvironmentSettingNames.WebsiteOwnerName, null },
                { EnvironmentSettingNames.AzureWebsiteSlotName, null },
                { EnvironmentSettingNames.FunctionAppVersion, null }
            });
        }
    }
}