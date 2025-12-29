using Microsoft.Azure.WebJobs.Script.Diagnostics.OpenTelemetry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics.OpenTelemetry
{
    public class FunctionsResourceDetectorTests : IDisposable
    {
        private readonly Dictionary<string, string> _originalEnvVars = new();
        private readonly FunctionsResourceDetector _detector;

        public FunctionsResourceDetectorTests()
        {
            _detector = new FunctionsResourceDetector();
            CaptureAndClearEnvironmentVariables();
        }

        public void Dispose()
        {
            RestoreEnvironmentVariables();
        }

        [Fact]
        public void Detect_ReturnsResource_WithServiceNameFromAssembly_WhenNoEnvironmentVariablesSet()
        {
            var resource = _detector.Detect();

            var attributes = resource.Attributes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            Assert.True(attributes.ContainsKey(ResourceSemanticConventions.ServiceName));
            Assert.True(attributes.ContainsKey(ResourceSemanticConventions.ServiceVersion));
            Assert.True(attributes.ContainsKey(ResourceSemanticConventions.ProcessId));
            Assert.True(attributes.ContainsKey(ResourceSemanticConventions.AISDKPrefix));
        }

        [Fact]
        public void Detect_ReturnsServiceName_FromOtelServiceNameEnvVar_WhenSet()
        {
            Environment.SetEnvironmentVariable(ResourceSemanticConventions.ServiceNameEnvVar, "CustomServiceName");

            var resource = _detector.Detect();

            var serviceName = resource.Attributes.FirstOrDefault(a => string.Equals(a.Key, ResourceSemanticConventions.ServiceName, StringComparison.Ordinal)).Value;
            Assert.Equal("CustomServiceName", serviceName);
        }

        [Fact]
        public void Detect_ReturnsServiceName_FromAzureWebsiteName_WhenOtelServiceNameNotSet()
        {
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName, "MyFunctionApp");

            var resource = _detector.Detect();

            var serviceName = resource.Attributes.FirstOrDefault(a => string.Equals(a.Key, ResourceSemanticConventions.ServiceName, StringComparison.Ordinal)).Value;
            Assert.Equal("MyFunctionApp", serviceName);
        }

        [Fact]
        public void Detect_OtelServiceName_TakesPrecedence_OverAzureWebsiteName()
        {
            Environment.SetEnvironmentVariable(ResourceSemanticConventions.ServiceNameEnvVar, "OtelServiceName");
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName, "AzureWebsiteName");

            var resource = _detector.Detect();

            var serviceName = resource.Attributes.FirstOrDefault(a => string.Equals(a.Key, ResourceSemanticConventions.ServiceName, StringComparison.Ordinal)).Value;
            Assert.Equal("OtelServiceName", serviceName);
        }

        [Fact]
        public void Detect_ReturnsServiceVersion_FromOtelServiceVersionEnvVar_WhenSet()
        {
            Environment.SetEnvironmentVariable(ResourceSemanticConventions.ServiceVersionEnvVar, "1.2.3");

            var resource = _detector.Detect();

            var serviceVersion = resource.Attributes.FirstOrDefault(a => string.Equals(a.Key, ResourceSemanticConventions.ServiceVersion, StringComparison.Ordinal)).Value;
            Assert.Equal("1.2.3", serviceVersion);
        }

        [Fact]
        public void Detect_IncludesAzureCloudAttributes_WhenAzureWebsiteNameIsSet()
        {
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName, "MyFunctionApp");

            var resource = _detector.Detect();

            var attributes = resource.Attributes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            Assert.Equal(OpenTelemetryConstants.AzureCloudProviderValue, attributes[ResourceSemanticConventions.CloudProvider]);
            Assert.Equal(OpenTelemetryConstants.AzurePlatformValue, attributes[ResourceSemanticConventions.CloudPlatform]);
        }

        [Fact]
        public void Detect_DoesNotIncludeAzureCloudAttributes_WhenAzureWebsiteNameIsNotSet()
        {
            var resource = _detector.Detect();

            var attributes = resource.Attributes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            Assert.False(attributes.ContainsKey(ResourceSemanticConventions.CloudProvider));
            Assert.False(attributes.ContainsKey(ResourceSemanticConventions.CloudPlatform));
        }

        [Fact]
        public void Detect_IncludesCloudRegion_WhenRegionNameIsSet()
        {
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName, "MyFunctionApp");
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.RegionName, "eastus");

            var resource = _detector.Detect();

            var region = resource.Attributes.FirstOrDefault(a => string.Equals(a.Key, ResourceSemanticConventions.CloudRegion, StringComparison.Ordinal)).Value;
            Assert.Equal("eastus", region);
        }

        [Fact]
        public void Detect_DoesNotIncludeCloudRegion_WhenRegionNameIsNotSet()
        {
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName, "MyFunctionApp");

            var resource = _detector.Detect();

            var attributes = resource.Attributes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            Assert.False(attributes.ContainsKey(ResourceSemanticConventions.CloudRegion));
        }

        [Fact]
        public void Detect_IncludesCloudResourceId_WhenResourceGroupAndSubscriptionAreSet()
        {
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName, "MyFunctionApp");
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.ResourceGroup, "my-rg");
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.WebsiteOwnerName, "sub-id-123+my-rg-westeurope");

            var resource = _detector.Detect();

            var resourceId = resource.Attributes.FirstOrDefault(a => string.Equals(a.Key, ResourceSemanticConventions.CloudResourceId, StringComparison.Ordinal)).Value;
            Assert.Equal("/subscriptions/sub-id-123/resourceGroups/my-rg/providers/Microsoft.Web/sites/MyFunctionApp", resourceId);
        }

        [Fact]
        public void Detect_DoesNotIncludeCloudResourceId_WhenResourceGroupIsNotSet()
        {
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName, "MyFunctionApp");
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.WebsiteOwnerName, "sub-id-123+my-rg-westeurope");

            var resource = _detector.Detect();

            var attributes = resource.Attributes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            Assert.False(attributes.ContainsKey(ResourceSemanticConventions.CloudResourceId));
        }

        [Fact]
        public void Detect_DoesNotIncludeCloudResourceId_WhenSubscriptionIdCannotBeParsed()
        {
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName, "MyFunctionApp");
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.ResourceGroup, "my-rg");
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.WebsiteOwnerName, "");

            var resource = _detector.Detect();

            var attributes = resource.Attributes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            Assert.False(attributes.ContainsKey(ResourceSemanticConventions.CloudResourceId));
        }

        [Fact]
        public void Detect_IncludesDeploymentEnvironmentName_WhenSlotNameIsSet()
        {
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName, "MyFunctionApp");
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSlotName, "staging");

            var resource = _detector.Detect();

            var slotName = resource.Attributes.FirstOrDefault(a => string.Equals(a.Key, ResourceSemanticConventions.DeploymentEnvironmentName, StringComparison.Ordinal)).Value;
            Assert.Equal("staging", slotName);
        }

        [Fact]
        public void Detect_DoesNotIncludeDeploymentEnvironmentName_WhenSlotNameIsNotSet()
        {
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName, "MyFunctionApp");

            var resource = _detector.Detect();

            var attributes = resource.Attributes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            Assert.False(attributes.ContainsKey(ResourceSemanticConventions.DeploymentEnvironmentName));
        }

        [Fact]
        public void Detect_IncludesAppDeploymentId_WhenFunctionAppVersionIsSet()
        {
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName, "MyFunctionApp");
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionAppVersion, "abc123");

            var resource = _detector.Detect();

            var appVersion = resource.Attributes.FirstOrDefault(a => string.Equals(a.Key, ResourceSemanticConventions.AppDeploymentId, StringComparison.Ordinal)).Value;
            Assert.Equal("abc123", appVersion);
        }

        [Fact]
        public void Detect_DoesNotIncludeAppDeploymentId_WhenFunctionAppVersionIsNotSet()
        {
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName, "MyFunctionApp");

            var resource = _detector.Detect();

            var attributes = resource.Attributes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            Assert.False(attributes.ContainsKey(ResourceSemanticConventions.AppDeploymentId));
        }

        [Fact]
        public void Detect_IncludesProcessId()
        {
            var resource = _detector.Detect();

            var attributes = resource.Attributes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            Assert.True(attributes.ContainsKey(ResourceSemanticConventions.ProcessId));
            Assert.IsType<Int64>(attributes[ResourceSemanticConventions.ProcessId]);
        }

        [Fact]
        public void Detect_IncludesAISDKPrefix_WithCorrectFormat()
        {
            var resource = _detector.Detect();

            var sdkPrefix = resource.Attributes.FirstOrDefault(a => string.Equals(a.Key, ResourceSemanticConventions.AISDKPrefix, StringComparison.Ordinal)).Value?.ToString();

            Assert.NotNull(sdkPrefix);
            Assert.StartsWith($"{OpenTelemetryConstants.SDKPrefix}:", sdkPrefix, StringComparison.Ordinal);
        }

        [Fact]
        public void Detect_ReturnsAllAzureAttributes_WhenAllEnvironmentVariablesAreSet()
        {
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName, "MyFunctionApp");
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.RegionName, "eastus");
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.ResourceGroup, "my-rg");
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.WebsiteOwnerName, "sub-id+my-rg-region");
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSlotName, "production");
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionAppVersion, "v1.0.0");

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
            Assert.True(attributes.ContainsKey(ResourceSemanticConventions.AppDeploymentId));
        }

        private void CaptureAndClearEnvironmentVariables()
        {
            string[] envVarNames =
            [
                ResourceSemanticConventions.ServiceNameEnvVar,
            ResourceSemanticConventions.ServiceVersionEnvVar,
            EnvironmentSettingNames.AzureWebsiteName,
            EnvironmentSettingNames.RegionName,
            EnvironmentSettingNames.ResourceGroup,
            EnvironmentSettingNames.WebsiteOwnerName,
            EnvironmentSettingNames.AzureWebsiteSlotName,
            EnvironmentSettingNames.FunctionAppVersion
            ];

            foreach (var name in envVarNames)
            {
                _originalEnvVars[name] = Environment.GetEnvironmentVariable(name);
                Environment.SetEnvironmentVariable(name, null);
            }
        }

        private void RestoreEnvironmentVariables()
        {
            foreach (var kvp in _originalEnvVars)
            {
                Environment.SetEnvironmentVariable(kvp.Key, kvp.Value);
            }
        }
    }
}
