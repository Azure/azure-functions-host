// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class ScriptApplicationHostOptionsSetupTests
    {
        [Fact]
        public void Configure_InStandbyMode_ReturnsExpectedConfiguration()
        {
            var options = CreateConfiguredOptions(true);

            Assert.EndsWith(@"functions\standby\logs", options.LogPath);
            Assert.EndsWith(@"functions\standby\wwwroot", options.ScriptPath);
            Assert.EndsWith(@"functions\standby\secrets", options.SecretsPath);
            Assert.False(options.IsSelfHost);
        }

        [Theory]
        [InlineData("1", true)]
        [InlineData("https://functionstest.blob.core.windows.net/microsoft/functionapp.zip", true)]
        [InlineData("https://functionstest.blob.core.windows.net/microsoft/functionapp.zip?sv=123434234234&other=key", true)]
        [InlineData("/microsoft/functionapp.zip", false)]
        [InlineData("functionapp.zip", false)]
        [InlineData("0", false)]
        [InlineData("", false)]
        public void IsZipDeployment_CorrectlyValidatesSetting(string appSettingValue, bool expectedOutcome)
        {
            var cloudBlockBlobService = new Mock<CloudBlockBlobHelperService>(MockBehavior.Strict);
            // This method is not used in this test case, but it's still called as part of configuration binding step, so we need to define the outcome
            cloudBlockBlobService.Setup(c => c.BlobExists(It.IsAny<string>())).ReturnsAsync(false);

            var zipSettings = new string[]
            {
                EnvironmentSettingNames.AzureWebsiteZipDeployment,
                EnvironmentSettingNames.AzureWebsiteAltZipDeployment,
                EnvironmentSettingNames.AzureWebsiteRunFromPackage
            };

            // Test each environment variable being set
            foreach (var setting in zipSettings)
            {
                var environment = new TestEnvironment();
                environment.SetEnvironmentVariable(setting, appSettingValue);

                var options = CreateConfiguredOptions(true, environment, cloudBlockBlobService.Object);

                Assert.Equal(options.IsFileSystemReadOnly, expectedOutcome);
                Assert.Equal(options.ScmRunFromPackageBlobExists, false);
            }

            // Test multiple being set
            var allSettingsEnvironment = new TestEnvironment();
            foreach (var setting in zipSettings)
            {
                allSettingsEnvironment.SetEnvironmentVariable(setting, appSettingValue);
            }

            var optionsAllSettings = CreateConfiguredOptions(true, allSettingsEnvironment, cloudBlockBlobService.Object);
            Assert.Equal(optionsAllSettings.IsFileSystemReadOnly, expectedOutcome);
        }

        [Theory]
        [InlineData("https://functionstest.blob.core.windows.net/microsoft/functionapp.zip", true)]
        [InlineData("/microsoft/functionapp.zip", false)]
        public void IsZipDeployment_ChecksScmRunFromPackageBlob(string appSettingValue, bool expectedOutcome)
        {
            var environment = new TestEnvironment();
            var cloudBlockBlobService = new Mock<CloudBlockBlobHelperService>(MockBehavior.Strict);
            cloudBlockBlobService.Setup(c => c.BlobExists(appSettingValue)).ReturnsAsync(true);
            cloudBlockBlobService.Setup(c => c.BlobExists(It.Is<string>(s => !string.Equals(s, appSettingValue, StringComparison.OrdinalIgnoreCase)))).ReturnsAsync(false);

            var options = CreateConfiguredOptions(true, environment, cloudBlockBlobService.Object);

            // No zip deployment settings set, it's not a zip deployment
            Assert.Equal(options.IsFileSystemReadOnly, false);

            // SCM_RUN_FROM_PACKAGE is set. If it's a valid URI, it's a zip deployment.
            environment.SetEnvironmentVariable(EnvironmentSettingNames.ScmRunFromPackage, appSettingValue);
            options = CreateConfiguredOptions(true, environment, cloudBlockBlobService.Object);
            Assert.Equal(options.IsFileSystemReadOnly, expectedOutcome);
        }

        private ScriptApplicationHostOptions CreateConfiguredOptions(bool inStandbyMode, IEnvironment environment = null, CloudBlockBlobHelperService cloudBlockBlobHelper = null)
        {
            var builder = new ConfigurationBuilder();
            var configuration = builder.Build();

            var standbyOptions = new TestOptionsMonitor<StandbyOptions>(new StandbyOptions { InStandbyMode = inStandbyMode });
            var mockCache = new Mock<IOptionsMonitorCache<ScriptApplicationHostOptions>>();
            var mockServiceProvider = new Mock<IServiceProvider>();
            var mockEnvironment = environment ?? new TestEnvironment();
            var mockCloudBlockBlobHelper = cloudBlockBlobHelper ?? new Mock<CloudBlockBlobHelperService>(MockBehavior.Default).Object;
            var setup = new ScriptApplicationHostOptionsSetup(configuration, standbyOptions, mockCache.Object, mockServiceProvider.Object, mockEnvironment, mockCloudBlockBlobHelper);

            var options = new ScriptApplicationHostOptions();
            setup.Configure(options);
            return options;
        }
    }
}
