// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks.Dataflow;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class HttpOptionsSetupTests
    {
        [Fact]
        public void Configure_Dynamic_AppService_Defaults()
        {
            var mockEnvironment = new Mock<IEnvironment>(MockBehavior.Strict);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteInstanceId)).Returns("1234");
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSku)).Returns(ScriptConstants.DynamicSku);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.FunctionsV2CompatibilityModeKey)).Returns<string>(null);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.FunctionsExtensionVersion)).Returns<string>(null);

            var setup = new HttpOptionsSetup(mockEnvironment.Object);
            var options = new HttpOptions();
            setup.Configure(options);

            Assert.True(options.DynamicThrottlesEnabled);
            Assert.Equal(HttpOptionsSetup.DefaultMaxConcurrentRequests, options.MaxConcurrentRequests);
            Assert.Equal(HttpOptionsSetup.DefaultMaxOutstandingRequests, options.MaxOutstandingRequests);
        }

        [Fact]
        public void Configure_Dynamic_NonAppService_Defaults()
        {
            var mockEnvironment = new Mock<IEnvironment>(MockBehavior.Strict);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteInstanceId)).Returns((string)null);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSku)).Returns(ScriptConstants.DynamicSku);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.FunctionsV2CompatibilityModeKey)).Returns<string>(null);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.FunctionsExtensionVersion)).Returns<string>(null);

            var setup = new HttpOptionsSetup(mockEnvironment.Object);
            var options = new HttpOptions();
            setup.Configure(options);

            Assert.False(options.DynamicThrottlesEnabled);
            Assert.Equal(HttpOptionsSetup.DefaultMaxConcurrentRequests, options.MaxConcurrentRequests);
            Assert.Equal(HttpOptionsSetup.DefaultMaxOutstandingRequests, options.MaxOutstandingRequests);
        }

        [Fact]
        public void Configure_Dedicated_DoesNotDefault()
        {
            var mockEnvironment = new Mock<IEnvironment>(MockBehavior.Strict);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteInstanceId)).Returns("1234");
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSku)).Returns("Dedicated");
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.FunctionsV2CompatibilityModeKey)).Returns<string>(null);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.FunctionsExtensionVersion)).Returns<string>(null);

            var setup = new HttpOptionsSetup(mockEnvironment.Object);
            var options = new HttpOptions();
            setup.Configure(options);

            Assert.False(options.DynamicThrottlesEnabled);
            Assert.Equal(DataflowBlockOptions.Unbounded, options.MaxConcurrentRequests);
            Assert.Equal(DataflowBlockOptions.Unbounded, options.MaxOutstandingRequests);
        }
    }
}
