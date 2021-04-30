// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class LinuxScriptApplicationHostOptionsSetupTests
    {
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

                var options = CreateConfiguredOptions(environment);

                Assert.Equal(options.IsFileSystemReadOnly, expectedOutcome);
                Assert.Equal(options.IsScmRunFromPackage, false);
            }

            // Test multiple being set
            var allSettingsEnvironment = new TestEnvironment();
            foreach (var setting in zipSettings)
            {
                allSettingsEnvironment.SetEnvironmentVariable(setting, appSettingValue);
            }

            var optionsAllSettings = CreateConfiguredOptions(allSettingsEnvironment);
            Assert.Equal(optionsAllSettings.IsFileSystemReadOnly, expectedOutcome);
        }

        [Theory]
        [InlineData("https://functionstest.blob.core.windows.net/microsoft/functionapp.zip", true)]
        [InlineData("/microsoft/functionapp.zip", false)]
        public void IsZipDeployment_ChecksScmRunFromPackageBlob(string appSettingValue, bool expectedOutcome)
        {
            var environment = new TestEnvironment();
            var options = CreateConfiguredOptions(environment, expectedOutcome);

            // No zip deployment settings set, it's not a zip deployment
            Assert.Equal(options.IsFileSystemReadOnly, false);

            // SCM_RUN_FROM_PACKAGE is set. If it's a valid URI, it's a zip deployment.
            environment.SetEnvironmentVariable(EnvironmentSettingNames.ScmRunFromPackage, appSettingValue);
            options = CreateConfiguredOptions(environment, expectedOutcome);
            Assert.Equal(options.IsFileSystemReadOnly, expectedOutcome);
        }

        private ScriptApplicationHostOptions CreateConfiguredOptions(IEnvironment environment = null, bool blobExists = false)
        {
            var mockEnvironment = environment ?? new TestEnvironment();
            var setup = new TestLinuxScriptApplicationOptionsSetup(mockEnvironment)
            {
                BlobExistsReturnValue = blobExists
            };

            var options = new ScriptApplicationHostOptions();
            setup.Configure(options);
            return options;
        }

        private class TestLinuxScriptApplicationOptionsSetup : LinuxScriptApplicationHostOptionsSetup
        {
            public TestLinuxScriptApplicationOptionsSetup(IEnvironment environment) : base(environment) { }

            public bool BlobExistsReturnValue { get; set; }

            public override bool BlobExists(string url)
            {
                return BlobExistsReturnValue;
            }
        }
    }
}
