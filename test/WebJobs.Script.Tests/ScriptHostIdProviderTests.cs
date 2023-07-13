// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class ScriptHostIdProviderTests
    {
        private readonly ScriptHostIdProvider _provider;
        private readonly Mock<IConfiguration> _mockConfiguration;
        private readonly HostIdValidator _hostIdValidator;
        private readonly TestEnvironment _environment;

        public ScriptHostIdProviderTests()
        {
            var options = new ScriptApplicationHostOptions();
            _environment = new TestEnvironment();

            _mockConfiguration = new Mock<IConfiguration>(MockBehavior.Strict);

            var loggerProvider = new TestLoggerProvider();
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(loggerProvider);
            var logger = loggerFactory.CreateLogger<HostIdValidator>();
            var hostNameProvider = new HostNameProvider(_environment);
            var mockStorageProvider = new Mock<IAzureBlobStorageProvider>(MockBehavior.Strict);
            var mockApplicationLifetime = new Mock<IApplicationLifetime>(MockBehavior.Strict);
            _hostIdValidator = new HostIdValidator(_environment, mockStorageProvider.Object, mockApplicationLifetime.Object, hostNameProvider, logger);
            _provider = new ScriptHostIdProvider(_mockConfiguration.Object, _environment, new TestOptionsMonitor<ScriptApplicationHostOptions>(options), _hostIdValidator);
        }

        [Fact]
        public async Task GetHostIdAsync_WithConfigurationHostId_ReturnsConfigurationHostId()
        {
            _mockConfiguration.SetupGet(p => p[ConfigurationSectionNames.HostIdPath]).Returns("test-host-id");

            string hostId = await _provider.GetHostIdAsync(CancellationToken.None);

            Assert.Equal("test-host-id", hostId);
        }

        [Theory]
        [InlineData("TEST-FUNCTIONS--", "123123", "test-functions", false)]
        [InlineData("TEST-FUNCTIONS-XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX", "123123", "test-functions-xxxxxxxxxxxxxxxxx", true)]
        public async Task GetHostIdAsync_AzureHost_ReturnsExpectedResult(string siteName, string azureWebsiteInstanceId, string expected, bool validationExpected)
        {
            _mockConfiguration.SetupGet(p => p[ConfigurationSectionNames.HostIdPath]).Returns((string)null);

            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteInstanceId, azureWebsiteInstanceId);
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName, siteName);
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.ContainerName, "testContainer");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHostName, "testsite.azurewebsites.net");

            string hostId = await _provider.GetHostIdAsync(CancellationToken.None);

            Assert.Equal(expected, hostId);
            Assert.Equal(validationExpected, _hostIdValidator.ValidationScheduled);
        }

        [Theory]
        [InlineData("TEST-FUNCTIONS--", "123123", "test-functions", false)]
        [InlineData("TEST-FUNCTIONS-XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX", "123123", "test-functions-xxxxxxxxxxxxxxxxx", true)]
        [InlineData("TEST-FUNCTIONS-XXXXXXXXXXXXXXXX-XXXX", "123123", "test-functions-xxxxxxxxxxxxxxxx", true)] /* 32nd character is a '-' */
        [InlineData("TEST-FUNCTIONS-XXXXXXXXXXXXXXXX-XXXX", null, "testsite", false)] // Linux consumption scenario where host id will be derived from the host name.
        [InlineData(null, "123123", null, false)]
        public void GetDefaultHostId_AzureHost_ReturnsExpectedResult(string siteName, string azureWebsiteInstanceId, string expected, bool expectedTruncation)
        {
            var options = new ScriptApplicationHostOptions
            {
                ScriptPath = @"c:\testscripts"
            };

            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteInstanceId, azureWebsiteInstanceId);
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName, siteName);
            environment.SetEnvironmentVariable(EnvironmentSettingNames.ContainerName, "testContainer");
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHostName, "testsite.azurewebsites.net");
            var result = ScriptHostIdProvider.GetDefaultHostId(environment, options);
            Assert.Equal(expected, result.HostId);
            Assert.Equal(expectedTruncation, result.IsTruncated);
            Assert.False(result.IsLocal);
        }

        [Theory]
        [InlineData("myfunctionstestapp", "6606e225f163a70190e4f4357d3dad78")]
        [InlineData("TEST-FUNCTIONS-XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX", "9dd2d6d54f1135ee6db6116f084b29bd")]
        [InlineData("TEST-FUNCTIONS-XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXA", "c842e18afa2a84eac2818a956687a72e")]
        [InlineData("TEST-FUNCTIONS-XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXB", "b595edd5b794d34e79c81361c823487c")]
        public void GetDefaultHostId_FlexConsumption_ReturnsExpectedResult(string siteName, string expected)
        {
            var options = new ScriptApplicationHostOptions
            {
                ScriptPath = @"c:\testscripts"
            };

            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSku, ScriptConstants.FlexConsumptionSku);
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName, siteName);

            var result = ScriptHostIdProvider.GetDefaultHostId(environment, options);

            Assert.Equal(expected, result.HostId);
            Assert.Equal(32, result.HostId.Length);
            Assert.False(result.IsTruncated);
        }

        [Fact]
        public void GetDefaultHostId_PlaceholderMode_ReturnsExpectedResult()
        {
            var options = new ScriptApplicationHostOptions
            {
                ScriptPath = @"c:\testscripts"
            };

            // In placeholder mode, site name and other settings aren't available to compute the
            // default HostId, so we expect null.
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteInstanceId, "123123");

            Assert.False(environment.IsFlexConsumptionSku());

            var result = ScriptHostIdProvider.GetDefaultHostId(environment, options);

            Assert.Equal(null, result.HostId);
        }

        [Fact]
        public void GetDefaultHostId_PlaceholderMode_FlexConsumption_ReturnsExpectedResult()
        {
            var options = new ScriptApplicationHostOptions
            {
                ScriptPath = @"c:\testscripts"
            };

            // In placeholder mode, site name and other settings aren't available to compute the
            // default HostId, so we expect null.
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.ContainerName, "testContainer");
            environment.SetEnvironmentVariable(EnvironmentSettingNames.LegionServiceHost, "legionhost");

            Assert.True(environment.IsFlexConsumptionSku());

            var result = ScriptHostIdProvider.GetDefaultHostId(environment, options);

            Assert.Equal(null, result.HostId);
        }

        [Fact]
        public void GetDefaultHostId_SelfHost_ReturnsExpectedResult()
        {
            var options = new ScriptApplicationHostOptions
            {
                IsSelfHost = true,
                ScriptPath = @"c:\testing\FUNCTIONS-TEST\test$#"
            };

            var environmentMock = new Mock<IEnvironment>();

            var result = ScriptHostIdProvider.GetDefaultHostId(environmentMock.Object, options);

            // This suffix is a stable hash code derived from the "RootScriptPath" string passed in the configuration.
            // We're using the literal here as we want this test to fail if this compuation ever returns something different.
            string suffix = "473716271";

            string sanitizedMachineName = Environment.MachineName
                    .Where(char.IsLetterOrDigit)
                    .Aggregate(new StringBuilder(), (b, c) => b.Append(c)).ToString().ToLowerInvariant();
            Assert.Equal($"{sanitizedMachineName}-{suffix}", result.HostId);
            Assert.True(result.IsLocal);
        }
    }
}
