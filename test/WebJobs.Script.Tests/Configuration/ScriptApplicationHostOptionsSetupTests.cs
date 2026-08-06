// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Azure.WebJobs.Script.Config.Tests;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class ScriptApplicationHostOptionsSetupTests
    {
        [Fact]
        public void IsFileSystemReadOnly_CanBeConfiguredExplicitly()
        {
            ScriptApplicationHostOptions options = new ScriptApplicationHostOptions();
            ConfiguredOptions(options, false);
            Assert.False(options.IsFileSystemReadOnly);

            options.IsFileSystemReadOnly = true;
            Assert.True(options.IsFileSystemReadOnly);
        }

        [Fact]
        public void Configure_InStandbyMode_ReturnsExpectedConfiguration()
        {
            ScriptApplicationHostOptions options = new ScriptApplicationHostOptions();
            ConfiguredOptions(options, true);

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
            ScriptApplicationHostOptions options = null;

            // Test each environment variable being set
            foreach (var setting in zipSettings)
            {
                var environment = new TestEnvironment();
                environment.SetEnvironmentVariable(setting, appSettingValue);

                options = new ScriptApplicationHostOptions();
                ConfiguredOptions(options, true, environment);

                Assert.Equal(options.IsFileSystemReadOnly, expectedOutcome);
            }

            // Test multiple being set
            var allSettingsEnvironment = new TestEnvironment();
            foreach (var setting in zipSettings)
            {
                allSettingsEnvironment.SetEnvironmentVariable(setting, appSettingValue);
            }

            options = new ScriptApplicationHostOptions();
            ConfiguredOptions(options, true, allSettingsEnvironment);
            Assert.Equal(options.IsFileSystemReadOnly, expectedOutcome);
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

            ScriptApplicationHostOptions options = new ScriptApplicationHostOptions();
            ConfiguredOptions(options, true, environment, expectedOutcome);

            // No zip deployment settings set, it's not a zip deployment
            Assert.Equal(options.IsFileSystemReadOnly, false);

            // SCM_RUN_FROM_PACKAGE is set. If it's a valid URI, it's a zip deployment.
            options = new ScriptApplicationHostOptions();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.ScmRunFromPackage, appSettingValue);
            ConfiguredOptions(options, true, environment, expectedOutcome);
            Assert.Equal(options.IsFileSystemReadOnly, expectedOutcome);
        }

        [Fact]
        public void IsFileSystemReadOnly_AlwaysAppliesForFlex()
        {
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSku, ScriptConstants.FlexConsumptionSku);

            ScriptApplicationHostOptions options = new ScriptApplicationHostOptions();
            ConfiguredOptions(options, inStandbyMode: false, environment);

            Assert.Equal(options.IsFileSystemReadOnly, true);
        }

        [Fact]
        public void IsFileSystemReadOnly_AlwaysAppliesForContainerAppsEnvironment()
        {
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.ManagedEnvironment, "true");

            ScriptApplicationHostOptions options = new ScriptApplicationHostOptions();
            ConfiguredOptions(options, inStandbyMode: false, environment);

            Assert.Equal(options.IsFileSystemReadOnly, true);
        }

        [Fact]
        public void ZipDeployment_CompleteMarkersRemainPhaseStableAndPackageInputsRemainLateBound()
        {
            foreach (EnvironmentProfileContract profile in
                EnvironmentBehaviorParityFixtures.CompleteProfiles)
            {
                foreach (HostPhase phase in Enum.GetValues<HostPhase>())
                {
                    Dictionary<string, string> variables = new(
                        profile.Markers,
                        StringComparer.Ordinal)
                    {
                        [EnvironmentSettingNames.AzureWebsitePlaceholderMode] =
                            EnvironmentBehaviorParityFixtures.IsPlaceholderPhase(phase)
                                ? "1"
                                : "0"
                    };
                    TestEnvironment environment = new(variables)
                    {
                        Platform = string.Equals(
                            profile.DefaultPlatform,
                            OSPlatform.Windows.ToString(),
                            StringComparison.Ordinal)
                                ? OSPlatform.Windows
                                : OSPlatform.Linux
                    };
                    TestScriptApplicationHostOptionsSetup setup =
                        CreateSetup(environment, blobExists: true);
                    environment.SetEnvironmentVariable(
                        EnvironmentSettingNames.AzureWebsiteRunFromPackage,
                        "1");
                    ScriptApplicationHostOptions options = new();

                    setup.Configure(
                        ScriptApplicationHostOptionsSetup.SkipPlaceholder,
                        options);

                    Assert.True(options.IsFileSystemReadOnly);
                    Assert.False(options.IsScmRunFromPackage);

                    environment.SetEnvironmentVariable(
                        EnvironmentSettingNames.AzureWebsiteRunFromPackage,
                        null);
                    environment.SetEnvironmentVariable(
                        EnvironmentSettingNames.ScmRunFromPackage,
                        "https://functionstest.blob.core.windows.net/microsoft/functionapp.zip");
                    options = new ScriptApplicationHostOptions();
                    setup.Configure(options);

                    bool linuxConsumption = profile.Profile is
                        HostingEnvironmentProfile.LinuxConsumptionAtlas
                        or HostingEnvironmentProfile.LinuxConsumptionLegion;
                    bool alwaysReadOnly = profile.Profile is
                        HostingEnvironmentProfile.FlexConsumptionLegion
                        or HostingEnvironmentProfile.ContainerApps;
                    Assert.Equal(
                        linuxConsumption || alwaysReadOnly,
                        options.IsFileSystemReadOnly);
                    Assert.Equal(
                        linuxConsumption,
                        options.IsScmRunFromPackage);

                    environment.SetEnvironmentVariable(
                        EnvironmentSettingNames.ScmRunFromPackage,
                        null);
                    options = new ScriptApplicationHostOptions
                    {
                        IsFileSystemReadOnly = true
                    };
                    setup.Configure(options);
                    Assert.True(options.IsFileSystemReadOnly);
                }
            }
        }

        private void ConfiguredOptions(ScriptApplicationHostOptions options, bool inStandbyMode, IEnvironment environment = null, bool blobExists = false)
        {
            var builder = new ConfigurationBuilder();
            var configuration = builder.Build();

            var standbyOptions = new TestOptionsMonitor<StandbyOptions>(new StandbyOptions { InStandbyMode = inStandbyMode });
            var mockServiceProvider = new Mock<IServiceProvider>();
            var mockEnvironment = environment ?? new TestEnvironment();
            var setup = new TestScriptApplicationHostOptionsSetup(configuration, standbyOptions, mockServiceProvider.Object, mockEnvironment)
            {
                BlobExistsReturnValue = blobExists
            };

            setup.Configure(options);
        }

        private static TestScriptApplicationHostOptionsSetup CreateSetup(
            TestEnvironment environment,
            bool blobExists)
        {
            IConfiguration configuration = new ConfigurationBuilder().Build();
            TestOptionsMonitor<StandbyOptions> standbyOptions = new(
                new StandbyOptions());
            Mock<IServiceProvider> serviceProvider = new();
            return new TestScriptApplicationHostOptionsSetup(
                configuration,
                standbyOptions,
                serviceProvider.Object,
                environment)
            {
                BlobExistsReturnValue = blobExists
            };
        }

        private class TestScriptApplicationHostOptionsSetup : ScriptApplicationHostOptionsSetup
        {
            public TestScriptApplicationHostOptionsSetup(IConfiguration configuration, IOptionsMonitor<StandbyOptions> standbyOptions,
                IServiceProvider serviceProvider, IEnvironment environment) : base(configuration, standbyOptions, serviceProvider, environment) { }

            public bool BlobExistsReturnValue { get; set; }

            public override bool BlobExists(string url)
            {
                return BlobExistsReturnValue;
            }
        }
    }
}
