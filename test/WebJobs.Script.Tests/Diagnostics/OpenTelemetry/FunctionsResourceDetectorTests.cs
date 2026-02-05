// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Diagnostics.OpenTelemetry;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics.OpenTelemetry;

public class FunctionsResourceDetectorTests
{
    private readonly FunctionsResourceDetector _detector = new();

    [Fact]
    public void Detect_ReturnsResource_WithServiceNameFromAssembly_WhenNoEnvironmentVariablesSet()
    {
        using var envVariables = SetupCleanEnvironment();

        var resource = _detector.Detect();

        var attributes = resource.Attributes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        Assert.True(attributes.ContainsKey(ResourceSemanticConventions.ServiceName));
        Assert.True(attributes.ContainsKey(ResourceSemanticConventions.ServiceVersion));
        Assert.True(attributes.ContainsKey(ResourceSemanticConventions.ProcessId));
        Assert.True(attributes.ContainsKey(ResourceSemanticConventions.AISDKPrefix));
    }

    [Fact]
    public void Detect_DoesNotIncludeServiceName_WhenOtelServiceNameEnvVarIsSet()
    {
        using var envVariables = new TestScopedEnvironmentVariable(ResourceSemanticConventions.ServiceNameEnvVar, "CustomServiceName");

        var resource = _detector.Detect();

        var attributes = resource.Attributes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        Assert.False(attributes.ContainsKey(ResourceSemanticConventions.ServiceName));
    }

    [Fact]
    public void Detect_DoesNotIncludeServiceName_WhenServiceNameInResourceAttributes()
    {
        using var envVariables = new TestScopedEnvironmentVariable(ResourceSemanticConventions.ResourceAttributeEnvVar, "service.name=CustomService,other=value");

        var resource = _detector.Detect();

        var attributes = resource.Attributes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        Assert.False(attributes.ContainsKey(ResourceSemanticConventions.ServiceName));
    }

    [Fact]
    public void Detect_DoesNotIncludeServiceName_WhenServiceNameInResourceAttributes_CaseSensitive()
    {
        using var envVariables = new TestScopedEnvironmentVariable(ResourceSemanticConventions.ResourceAttributeEnvVar, "Service.Name=CustomService,other=value");

        var resource = _detector.Detect();

        var attributes = resource.Attributes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        Assert.True(attributes.ContainsKey(ResourceSemanticConventions.ServiceName));
    }

    [Fact]
    public void Detect_ReturnsServiceName_FromAzureWebsiteName_WhenOtelServiceNameNotSet()
    {
        using var envVariables = new TestScopedEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName, "MyFunctionApp");

        var resource = _detector.Detect();

        var serviceName = resource.Attributes.FirstOrDefault(a => string.Equals(a.Key, ResourceSemanticConventions.ServiceName, StringComparison.Ordinal)).Value;

        Assert.Equal("MyFunctionApp", serviceName);
    }

    [Fact]
    public void Detect_OtelServiceName_TakesPrecedence_OverAzureWebsiteName()
    {
        using var envVariables = new TestScopedEnvironmentVariable(new Dictionary<string, string>
        {
            { ResourceSemanticConventions.ServiceNameEnvVar, "OtelServiceName" },
            { EnvironmentSettingNames.AzureWebsiteName, "AzureWebsiteName" }
        });

        var resource = _detector.Detect();

        var attributes = resource.Attributes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        Assert.False(attributes.ContainsKey(ResourceSemanticConventions.ServiceName));
    }

    [Fact]
    public void Detect_DoesNotIncludeServiceVersion_WhenServiceVersionInResourceAttributes()
    {
        using var envVariables = new TestScopedEnvironmentVariable(ResourceSemanticConventions.ResourceAttributeEnvVar, "service.version=1.0.0,other=value");

        var resource = _detector.Detect();

        var attributes = resource.Attributes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        Assert.False(attributes.ContainsKey(ResourceSemanticConventions.ServiceVersion));
    }

    [Fact]
    public void Detect_IncludesAzureCloudAttributes_WhenAzureWebsiteNameIsSet()
    {
        using var envVariables = new TestScopedEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName, "MyFunctionApp");

        var resource = _detector.Detect();

        var attributes = resource.Attributes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        Assert.Equal(OpenTelemetryConstants.AzureCloudProviderValue, attributes[ResourceSemanticConventions.CloudProvider]);
        Assert.Equal(OpenTelemetryConstants.AzurePlatformValue, attributes[ResourceSemanticConventions.CloudPlatform]);
    }

    [Fact]
    public void Detect_DoesNotIncludeAzureCloudAttributes_WhenAzureWebsiteNameIsNotSet()
    {
        using var envVariables = SetupCleanEnvironment();

        var resource = _detector.Detect();

        var attributes = resource.Attributes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        Assert.False(attributes.ContainsKey(ResourceSemanticConventions.CloudProvider));
        Assert.False(attributes.ContainsKey(ResourceSemanticConventions.CloudPlatform));
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

        var region = resource.Attributes.FirstOrDefault(a => string.Equals(a.Key, ResourceSemanticConventions.CloudRegion, StringComparison.Ordinal)).Value;

        Assert.Equal("eastus", region);
    }

    [Fact]
    public void Detect_DoesNotIncludeCloudRegion_WhenRegionNameIsNotSet()
    {
        using var envVariables = new TestScopedEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName, "MyFunctionApp");

        var resource = _detector.Detect();

        var attributes = resource.Attributes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        Assert.False(attributes.ContainsKey(ResourceSemanticConventions.CloudRegion));
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

        var resourceId = resource.Attributes.FirstOrDefault(a => string.Equals(a.Key, ResourceSemanticConventions.CloudResourceId, StringComparison.Ordinal)).Value;

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

        Assert.False(attributes.ContainsKey(ResourceSemanticConventions.CloudResourceId));
    }

    [Fact]
    public void Detect_DoesNotIncludeCloudResourceId_WhenSubscriptionIdCannotBeParsed()
    {
        using var envVariables = new TestScopedEnvironmentVariable(new Dictionary<string, string>
        {
            { EnvironmentSettingNames.AzureWebsiteName, "MyFunctionApp" },
            { EnvironmentSettingNames.ResourceGroup, "my-rg" },
            { EnvironmentSettingNames.WebsiteOwnerName, string.Empty }
        });

        var resource = _detector.Detect();

        var attributes = resource.Attributes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        Assert.False(attributes.ContainsKey(ResourceSemanticConventions.CloudResourceId));
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

        var slotName = resource.Attributes.FirstOrDefault(a => string.Equals(a.Key, ResourceSemanticConventions.DeploymentEnvironmentName, StringComparison.Ordinal)).Value;

        Assert.Equal("staging", slotName);
    }

    [Fact]
    public void Detect_DoesNotIncludeDeploymentEnvironmentName_WhenSlotNameIsNotSet()
    {
        using var envVariables = new TestScopedEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName, "MyFunctionApp");

        var resource = _detector.Detect();

        var attributes = resource.Attributes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        Assert.False(attributes.ContainsKey(ResourceSemanticConventions.DeploymentEnvironmentName));
    }

    [Fact]
    public void Detect_IncludesAppDeploymentId_WhenFunctionAppVersionIsSet()
    {
        using var envVariables = new TestScopedEnvironmentVariable(new Dictionary<string, string>
        {
            { EnvironmentSettingNames.AzureWebsiteName, "MyFunctionApp" },
            { EnvironmentSettingNames.FunctionsSiteUpdateId, "abc123" }
        });

        var resource = _detector.Detect();

        var appVersion = resource.Attributes.FirstOrDefault(a => string.Equals(a.Key, ResourceSemanticConventions.SiteUpdateId, StringComparison.Ordinal)).Value;

        Assert.Equal("abc123", appVersion);
    }

    [Fact]
    public void Detect_DoesNotIncludeAppDeploymentId_WhenFunctionAppVersionIsNotSet()
    {
        using var envVariables = new TestScopedEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName, "MyFunctionApp");

        var resource = _detector.Detect();

        var attributes = resource.Attributes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        Assert.False(attributes.ContainsKey(ResourceSemanticConventions.SiteUpdateId));
    }

    [Fact]
    public void Detect_IncludesProcessId()
    {
        using var envVariables = SetupCleanEnvironment();

        var resource = _detector.Detect();

        var attributes = resource.Attributes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        Assert.True(attributes.TryGetValue(ResourceSemanticConventions.ProcessId, out var processId));
        Assert.IsType<long>(processId);
    }

    [Fact]
    public void Detect_IncludesAISDKPrefix_WithCorrectFormat()
    {
        using var envVariables = SetupCleanEnvironment();

        var resource = _detector.Detect();

        var sdkPrefix = resource.Attributes.FirstOrDefault(a => string.Equals(a.Key, ResourceSemanticConventions.AISDKPrefix, StringComparison.Ordinal)).Value?.ToString();

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
            { EnvironmentSettingNames.FunctionsSiteUpdateId, "v1.0.0" }
        });

        var resource = _detector.Detect();

        var attributes = resource.Attributes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        Assert.True(attributes.ContainsKey(ResourceSemanticConventions.ServiceName));
        Assert.True(attributes.ContainsKey(ResourceSemanticConventions.ServiceVersion));
        Assert.True(attributes.ContainsKey(ResourceSemanticConventions.ProcessId));
        Assert.True(attributes.ContainsKey(ResourceSemanticConventions.AISDKPrefix));
        Assert.True(attributes.ContainsKey(ResourceSemanticConventions.CloudProvider));
        Assert.True(attributes.ContainsKey(ResourceSemanticConventions.CloudPlatform));
        Assert.True(attributes.ContainsKey(ResourceSemanticConventions.CloudRegion));
        Assert.True(attributes.ContainsKey(ResourceSemanticConventions.CloudResourceId));
        Assert.True(attributes.ContainsKey(ResourceSemanticConventions.DeploymentEnvironmentName));
        Assert.True(attributes.ContainsKey(ResourceSemanticConventions.SiteUpdateId));
    }

    private static IDisposable SetupCleanEnvironment()
    {
        return new TestScopedEnvironmentVariable(new Dictionary<string, string>
        {
            { ResourceSemanticConventions.ServiceNameEnvVar, null },
            { ResourceSemanticConventions.ResourceAttributeEnvVar, null },
            { EnvironmentSettingNames.AzureWebsiteName, null },
            { EnvironmentSettingNames.RegionName, null },
            { EnvironmentSettingNames.ResourceGroup, null },
            { EnvironmentSettingNames.WebsiteOwnerName, null },
            { EnvironmentSettingNames.AzureWebsiteSlotName, null },
            { EnvironmentSettingNames.FunctionsSiteUpdateId, null }
        });
    }

    [Fact]
    public void Detect_IncludesServiceName_WhenOtelServiceNameEnvVarIsNull()
    {
        using var envVariables = new TestScopedEnvironmentVariable(new Dictionary<string, string>
        {
            { ResourceSemanticConventions.ServiceNameEnvVar, null },
            { EnvironmentSettingNames.AzureWebsiteName, "MyFunctionApp" }
        });

        var resource = _detector.Detect();

        var serviceName = resource.Attributes.FirstOrDefault(a => string.Equals(a.Key, ResourceSemanticConventions.ServiceName, StringComparison.Ordinal)).Value;

        Assert.Equal("MyFunctionApp", serviceName);
    }

    [Fact]
    public void Detect_IncludesServiceName_WhenOtelServiceNameEnvVarIsEmpty()
    {
        using var envVariables = new TestScopedEnvironmentVariable(new Dictionary<string, string>
        {
            { ResourceSemanticConventions.ServiceNameEnvVar, string.Empty },
            { EnvironmentSettingNames.AzureWebsiteName, "MyFunctionApp" }
        });

        var resource = _detector.Detect();

        var serviceName = resource.Attributes.FirstOrDefault(a => string.Equals(a.Key, ResourceSemanticConventions.ServiceName, StringComparison.Ordinal)).Value;

        Assert.Equal("MyFunctionApp", serviceName);
    }

    [Fact]
    public void Detect_IncludesServiceName_WhenResourceAttributeEnvVarIsNull()
    {
        using var envVariables = new TestScopedEnvironmentVariable(new Dictionary<string, string>
        {
            { ResourceSemanticConventions.ResourceAttributeEnvVar, null },
            { EnvironmentSettingNames.AzureWebsiteName, "MyFunctionApp" }
        });

        var resource = _detector.Detect();

        var serviceName = resource.Attributes.FirstOrDefault(a => string.Equals(a.Key, ResourceSemanticConventions.ServiceName, StringComparison.Ordinal)).Value;

        Assert.Equal("MyFunctionApp", serviceName);
    }

    [Fact]
    public void Detect_IncludesServiceName_WhenResourceAttributeEnvVarIsEmpty()
    {
        using var envVariables = new TestScopedEnvironmentVariable(new Dictionary<string, string>
        {
            { ResourceSemanticConventions.ResourceAttributeEnvVar, string.Empty },
            { EnvironmentSettingNames.AzureWebsiteName, "MyFunctionApp" }
        });

        var resource = _detector.Detect();

        var serviceName = resource.Attributes.FirstOrDefault(a => string.Equals(a.Key, ResourceSemanticConventions.ServiceName, StringComparison.Ordinal)).Value;

        Assert.Equal("MyFunctionApp", serviceName);
    }

    [Theory]
    [InlineData("otel-service")]
    [InlineData("service.name=attr-service")]
    public void Detect_DoesNotIncludeServiceName_WhenServiceNameIsConfigured(string resourceAttributesOrServiceName)
    {
        // Test with OTEL_SERVICE_NAME
        if (!resourceAttributesOrServiceName.Contains('='))
        {
            using var serviceNameVar = new TestScopedEnvironmentVariable(ResourceSemanticConventions.ServiceNameEnvVar, resourceAttributesOrServiceName);

            var resource = _detector.Detect();
            var attributes = resource.Attributes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            Assert.False(attributes.ContainsKey(ResourceSemanticConventions.ServiceName));
        }
        // Test with OTEL_RESOURCE_ATTRIBUTES
        else
        {
            using var resourceAttrVar = new TestScopedEnvironmentVariable(ResourceSemanticConventions.ResourceAttributeEnvVar, resourceAttributesOrServiceName);

            var resource = _detector.Detect();
            var attributes = resource.Attributes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            Assert.False(attributes.ContainsKey(ResourceSemanticConventions.ServiceName));
        }
    }

    [Theory]
    [InlineData(null, "my-function-app", "my-function-app")]
    [InlineData(" ", "my-function-app", "my-function-app")]
    [InlineData(null, null, "Microsoft.Azure.WebJobs.Script")] // Replace with actual assembly name
    public void Detect_IncludesServiceName_WhenServiceNameNotConfigured(string resourceAttributes, string websiteName, string expectedServiceName)
    {
        using var resourceAttrVar = new TestScopedEnvironmentVariable(ResourceSemanticConventions.ResourceAttributeEnvVar, resourceAttributes);
        using var websiteNameVar = new TestScopedEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName, websiteName);

        var resource = _detector.Detect();
        var attributes = resource.Attributes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        Assert.True(attributes.ContainsKey(ResourceSemanticConventions.ServiceName));
        Assert.Equal(expectedServiceName, attributes[ResourceSemanticConventions.ServiceName]);
    }

    [Fact]
    public void Detect_DoesNotIncludeServiceName_WhenOtelServiceNameTakesPrecedence()
    {
        using var serviceNameVar = new TestScopedEnvironmentVariable(ResourceSemanticConventions.ServiceNameEnvVar, "otel-service");
        using var resourceAttrVar = new TestScopedEnvironmentVariable(ResourceSemanticConventions.ResourceAttributeEnvVar, "service.name=attr-service");

        var resource = _detector.Detect();
        var attributes = resource.Attributes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        // When OTEL_SERVICE_NAME is set, detector doesn't add service.name (SDK handles it)
        Assert.False(attributes.ContainsKey(ResourceSemanticConventions.ServiceName));
    }

    [Theory]
    [InlineData("service.version=2.0.0", false, true)] // Configured in resource attributes
    [InlineData("service.version=2.0.0,service.name=ABC", false, false)]
    [InlineData("service.name=ABC", true, false)]
    [InlineData("other=value", true, true)] // Not configured
    [InlineData(null, true, true)] // Not configured
    [InlineData("", true, true)] // Not configured
    public void Detect_HandlesServiceVersion(string resourceAttributes, bool shouldIncludeVersion, bool shouldIncludeName)
    {
        using var envVar = new TestScopedEnvironmentVariable(ResourceSemanticConventions.ResourceAttributeEnvVar, resourceAttributes);

        var resource = _detector.Detect();
        var attributes = resource.Attributes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        Assert.Equal(shouldIncludeVersion, attributes.ContainsKey(ResourceSemanticConventions.ServiceVersion));
        Assert.Equal(shouldIncludeName, attributes.ContainsKey(ResourceSemanticConventions.ServiceName));
    }

    [Theory]
    [InlineData("my-app", "my-rg", "sub-123+extra", "/subscriptions/sub-123/resourceGroups/my-rg/providers/Microsoft.Web/sites/my-app")]
    [InlineData("my-app", "my-rg", "sub-456", "/subscriptions/sub-456/resourceGroups/my-rg/providers/Microsoft.Web/sites/my-app")] // No plus sign
    [InlineData("different-app", "different-rg", "sub-789+data", "/subscriptions/sub-789/resourceGroups/different-rg/providers/Microsoft.Web/sites/different-app")]
    public void Detect_IncludesAzureResourceUri_WhenEnvironmentVariablesAreSet(string websiteName, string resourceGroup, string owner, string expectedUri)
    {
        using var websiteNameVar = new TestScopedEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName, websiteName);
        using var resourceGroupVar = new TestScopedEnvironmentVariable(EnvironmentSettingNames.ResourceGroup, resourceGroup);
        using var ownerVar = new TestScopedEnvironmentVariable(EnvironmentSettingNames.WebsiteOwnerName, owner);

        var resource = _detector.Detect();
        var attributes = resource.Attributes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        Assert.Equal(expectedUri, attributes[ResourceSemanticConventions.CloudResourceId]);
    }

    [Theory]
    [InlineData("my-app", "", "")]
    [InlineData("", "", "")]
    [InlineData("my-app", "", "B")]
    [InlineData("my-app", "A", "")]
    [InlineData(null, null, null)]
    [InlineData("my-app", null, null)]
    [InlineData("my-app", "A", null)]
    [InlineData("my-app", null, "B")]
    public void Detect_IncludesAzureResourceUri_WhenEnvironmentVariablesAreNotSet(string websiteName, string resourceGroup, string owner)
    {
        using var websiteNameVar = new TestScopedEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName, websiteName);
        using var resourceGroupVar = new TestScopedEnvironmentVariable(EnvironmentSettingNames.ResourceGroup, resourceGroup);
        using var ownerVar = new TestScopedEnvironmentVariable(EnvironmentSettingNames.WebsiteOwnerName, owner);

        var resource = _detector.Detect();
        var attributes = resource.Attributes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        Assert.False(attributes.ContainsKey(ResourceSemanticConventions.CloudResourceId));
    }

    [Theory]
    [InlineData(EnvironmentSettingNames.RegionName, "eastus", ResourceSemanticConventions.CloudRegion)]
    [InlineData(EnvironmentSettingNames.AzureWebsiteSlotName, "staging", ResourceSemanticConventions.DeploymentEnvironmentName)]
    [InlineData(EnvironmentSettingNames.FunctionsSiteUpdateId, "v123", ResourceSemanticConventions.SiteUpdateId)]
    public void Detect_IncludesOptionalAzureAttribute_WhenSet(string envVarName, string envVarValue, string expectedAttributeKey)
    {
        using var websiteName = new TestScopedEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName, "my-app");
        using var optionalVar = new TestScopedEnvironmentVariable(envVarName, envVarValue);

        var resource = _detector.Detect();
        var attributes = resource.Attributes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        Assert.Equal(envVarValue, attributes[expectedAttributeKey]);
    }
}