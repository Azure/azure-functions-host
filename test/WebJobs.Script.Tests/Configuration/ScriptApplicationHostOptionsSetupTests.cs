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

                var options = CreateConfiguredOptions(true, environment);

                Assert.Equal(options.IsFileSystemReadOnly, expectedOutcome);
            }

            // Test multiple being set
            var allSettingsEnvironment = new TestEnvironment();
            foreach (var setting in zipSettings)
            {
                allSettingsEnvironment.SetEnvironmentVariable(setting, appSettingValue);
            }

            var optionsAllSettings = CreateConfiguredOptions(true, allSettingsEnvironment);
            Assert.Equal(optionsAllSettings.IsFileSystemReadOnly, expectedOutcome);
        }

        [Theory]
        [InlineData("https://functionstest.blob.core.windows.net/microsoft/functionapp.zip", true)]
        [InlineData("https://functionstest.blob.core.windows.net/microsoft/functionapp2.zip", false)]
        [InlineData("/microsoft/functionapp.zip", false)]
        public void IsZipDeployment_ChecksScmRunFromPackageBlob(string appSettingValue, bool expectedOutcome)
        {
            var environment = new TestEnvironment();
            // Linux Consumption-specific tests, ensure environment reflects that.
            environment.SetEnvironmentVariable(EnvironmentSettingNames.ContainerName, "test-container");

            var options = CreateConfiguredOptions(true, environment, expectedOutcome);

            // No zip deployment settings set, it's not a zip deployment
            Assert.Equal(options.IsFileSystemReadOnly, false);

            // SCM_RUN_FROM_PACKAGE is set. If it's a valid URI, it's a zip deployment.
            environment.SetEnvironmentVariable(EnvironmentSettingNames.ScmRunFromPackage, appSettingValue);
            options = CreateConfiguredOptions(true, environment, expectedOutcome);
            Assert.Equal(options.IsFileSystemReadOnly, expectedOutcome);
        }

        private ScriptApplicationHostOptions CreateConfiguredOptions(bool inStandbyMode, IEnvironment environment = null, bool blobExists = false)
        {
            var builder = new ConfigurationBuilder();
            var configuration = builder.Build();

            var standbyOptions = new TestOptionsMonitor<StandbyOptions>(new StandbyOptions { InStandbyMode = inStandbyMode });
            var mockCache = new Mock<IOptionsMonitorCache<ScriptApplicationHostOptions>>();
            var mockServiceProvider = new Mock<IServiceProvider>();
            var mockEnvironment = environment ?? new TestEnvironment();
            var setup = new TestScriptApplicationHostOptionsSetup(configuration, standbyOptions, mockCache.Object, mockServiceProvider.Object, mockEnvironment)
            {
                BlobExistsReturnValue = blobExists
            };

            var options = new ScriptApplicationHostOptions();
            setup.Configure(options);
            return options;
        }

        private class TestScriptApplicationHostOptionsSetup : ScriptApplicationHostOptionsSetup
        {
            public TestScriptApplicationHostOptionsSetup(IConfiguration configuration, IOptionsMonitor<StandbyOptions> standbyOptions, IOptionsMonitorCache<ScriptApplicationHostOptions> cache,
                IServiceProvider serviceProvider, IEnvironment environment) : base(configuration, standbyOptions, cache, serviceProvider, environment) { }

            public bool BlobExistsReturnValue { get; set; }

            public override bool BlobExists(string url)
            {
                return BlobExistsReturnValue;
            }
        }
    }
}
