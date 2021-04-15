//// Copyright (c) .NET Foundation. All rights reserved.
//// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class FileSystemManagerTests
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
                var fileSystemManager = new FileSystemManager(environment);

                Assert.Equal(fileSystemManager.IsZipDeployment(MockNullLoggerFactory.CreateLogger()), expectedOutcome);
            }

            // Test multiple being set
            var allSettingsEnvironment = new TestEnvironment();

            foreach (var setting in zipSettings)
            {
                allSettingsEnvironment.SetEnvironmentVariable(setting, appSettingValue);
            }

            var fileSystemManagerAllSettings = new FileSystemManager(allSettingsEnvironment);
            Assert.Equal(fileSystemManagerAllSettings.IsZipDeployment(MockNullLoggerFactory.CreateLogger()), expectedOutcome);
        }

        [Theory]
        [InlineData("https://functionstest.blob.core.windows.net/microsoft/functionapp.zip", true)]
        public void CacheIfBlobExists_CallsStorageOnlyOnce(string appSettingValue, bool expectedOutcome)
        {
            var environment = new TestEnvironment();

            var envSettings = new string[]
            {
                EnvironmentSettingNames.ScmRunFromPackage,
                EnvironmentSettingNames.AzureFilesConnectionString,
                EnvironmentSettingNames.AzureFilesContentShare
            };

            foreach (var setting in envSettings)
            {
                environment.SetEnvironmentVariable(setting, appSettingValue);
            }

            var cloudBlockBlobService = new Mock<CloudBlockBlobHelperService>(MockBehavior.Strict);
            cloudBlockBlobService
                .Setup(c => c.BlobExists(appSettingValue, EnvironmentSettingNames.ScmRunFromPackage, NullLogger.Instance))
                .ReturnsAsync(expectedOutcome);

            var fileSystemManager = new FileSystemManager(environment, cloudBlockBlobService.Object);

            fileSystemManager.CacheIfBlobExists(NullLogger.Instance);
            fileSystemManager.CacheIfBlobExists(NullLogger.Instance);
            cloudBlockBlobService.Verify(x => x.BlobExists(appSettingValue, EnvironmentSettingNames.ScmRunFromPackage, NullLogger.Instance), Times.Once());
        }
    }
}
