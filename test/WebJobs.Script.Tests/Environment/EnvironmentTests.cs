// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Runtime.InteropServices;
using Xunit;
using static Microsoft.Azure.WebJobs.Script.EnvironmentSettingNames;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class EnvironmentTests
    {
        [Fact]
        public void IsWindowsAzureManagedHosting_SetAzureWebsiteInstanceId_ReturnsTrue()
        {
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(AzureWebsiteInstanceId, Guid.NewGuid().ToString("N"));
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.True(environment.IsWindowsAzureManagedHosting());
            }
            else
            {
                Assert.False(environment.IsWindowsAzureManagedHosting());
            }
        }

        [Fact]
        public void IsWindowsAzureManagedHosting_AzureWebsiteInstanceIdNotSet_ReturnsFalse()
        {
            var environment = new TestEnvironment();
            Assert.False(environment.IsWindowsAzureManagedHosting());
        }

        [Fact]
        public void IsCoreTools_SetAzureWebsiteInstanceId_ReturnsTrue()
        {
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(CoreToolsEnvironment, "true");
            Assert.True(environment.IsCoreTools());
        }

        [Fact]
        public void IsLinuxAppServiceEnvWithPersistentStorage_NotLinux_ReturnsFalse()
        {
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(AzureWebsiteInstanceId, Guid.NewGuid().ToString("N"));
            Assert.False(environment.IsLinuxAppServiceWithPersistentFileSystem());
        }

        [Fact]
        public void IsLinuxAppServiceEnvWithPersistentStorage_LinuxStorageSettingNotPresent_ReturnsTrue()
        {
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(AzureWebsiteInstanceId, Guid.NewGuid().ToString("N"));
            environment.SetEnvironmentVariable(FunctionsLogsMountPath, Guid.NewGuid().ToString("N"));
            Assert.True(environment.IsLinuxAppServiceWithPersistentFileSystem());
        }

        [Fact]
        public void IsLinuxAppServiceEnvWithPersistentStorage_StorageSetToFalse_ReturnsFalse()
        {
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(AzureWebsiteInstanceId, Guid.NewGuid().ToString("N"));
            environment.SetEnvironmentVariable(FunctionsLogsMountPath, Guid.NewGuid().ToString("N"));
            environment.SetEnvironmentVariable(LinuxAzureAppServiceStorage, "false");
            Assert.False(environment.IsLinuxAppServiceWithPersistentFileSystem());
        }

        [Fact]
        public void IsLinuxAppServiceEnvWithPersistentStorage_NotLinuxWithStorageSet_ReturnsFalse()
        {
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(AzureWebsiteInstanceId, Guid.NewGuid().ToString("N"));
            environment.SetEnvironmentVariable(LinuxAzureAppServiceStorage, "true");
            Assert.False(environment.IsLinuxAppServiceWithPersistentFileSystem());
        }

        [Fact]
        public void IsLinuxAppServiceEnvWithPersistentStorage_StorageSetToTrue_ReturnsTrue()
        {
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(AzureWebsiteInstanceId, Guid.NewGuid().ToString("N"));
            environment.SetEnvironmentVariable(FunctionsLogsMountPath, Guid.NewGuid().ToString("N"));
            environment.SetEnvironmentVariable(LinuxAzureAppServiceStorage, "true");
            Assert.True(environment.IsLinuxAppServiceWithPersistentFileSystem());
        }

        [Fact]
        public void IsPersistentStorageAvailable_IsWindows_ReturnsTrue()
        {
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(AzureWebsiteInstanceId, Guid.NewGuid().ToString("N"));
            Assert.True(environment.IsWindowsAzureManagedHosting());
            Assert.True(environment.IsPersistentFileSystemAvailable());
        }

        [Fact]
        public void IsPersistentStorageAvailable_IsCoreTools_ReturnsTrue()
        {
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(CoreToolsEnvironment, "true");
            Assert.True(environment.IsPersistentFileSystemAvailable());
        }

        [Fact]
        public void IsContainer_valid_ReturnsTrue()
        {
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(RunningInContainer, "true");
            Assert.True(environment.IsContainer());
        }

        [Theory]
        [InlineData("false")]
        [InlineData(null)]
        public void IsContainer_Invalid_ReturnsFalse(string runningInContainerValue)
        {
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(RunningInContainer, runningInContainerValue);
            Assert.False(environment.IsContainer());
        }

        [Fact]
        public void IsPersistentStorageAvailable_IsLinuxWithoutStorage_ReturnsFalse()
        {
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(AzureWebsiteInstanceId, Guid.NewGuid().ToString("N"));
            environment.SetEnvironmentVariable(FunctionsLogsMountPath, Guid.NewGuid().ToString("N"));
            environment.SetEnvironmentVariable(LinuxAzureAppServiceStorage, "false");
            Assert.False(environment.IsLinuxAppServiceWithPersistentFileSystem());
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.True(environment.IsPersistentFileSystemAvailable());
            }
            else
            {
                Assert.False(environment.IsPersistentFileSystemAvailable());
            }
        }

        [Fact]
        public void IsPersistentStorageAvailable_IsLinuxWithStorage_ReturnsTrue()
        {
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(AzureWebsiteInstanceId, Guid.NewGuid().ToString("N"));
            environment.SetEnvironmentVariable(FunctionsLogsMountPath, Guid.NewGuid().ToString("N"));
            Assert.True(environment.IsLinuxAppServiceWithPersistentFileSystem());
            Assert.True(environment.IsPersistentFileSystemAvailable());
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
                Assert.Equal(environment.IsZipDeployment(), expectedOutcome);
            }

            // Test multiple being set
            var allSettingsEnvironment = new TestEnvironment();

            foreach (var setting in zipSettings)
            {
                allSettingsEnvironment.SetEnvironmentVariable(setting, appSettingValue);
            }

            Assert.Equal(allSettingsEnvironment.IsZipDeployment(), expectedOutcome);
        }

        [Theory]
        [InlineData("Azure", CloudName.Azure)]
        [InlineData("azuRe", CloudName.Azure)]
        [InlineData("", CloudName.Azure)]
        [InlineData(null, CloudName.Azure)]
        [InlineData("Blackforest", CloudName.Blackforest)]
        [InlineData("Fairfax", CloudName.Fairfax)]
        [InlineData("Mooncake", CloudName.Mooncake)]
        [InlineData("USNat", CloudName.USNat)]
        [InlineData("USSec", CloudName.USSec)]
        public void GetCloudName_Returns_RightCloud(string cloudNameSetting, CloudName cloudName)
        {
            var testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.CloudName, cloudNameSetting);
            Assert.Equal(cloudName, testEnvironment.GetCloudName());
        }

        [Theory]
        [InlineData("Azure", CloudConstants.AzureStorageSuffix)]
        [InlineData("azuRe", CloudConstants.AzureStorageSuffix)]
        [InlineData("", CloudConstants.AzureStorageSuffix)]
        [InlineData(null, CloudConstants.AzureStorageSuffix)]
        [InlineData("Blackforest", CloudConstants.BlackforestStorageSuffix)]
        [InlineData("Fairfax", CloudConstants.FairfaxStorageSuffix)]
        [InlineData("Mooncake", CloudConstants.MooncakeStorageSuffix)]
        [InlineData("USNat", CloudConstants.USNatStorageSuffix)]
        [InlineData("USSec", CloudConstants.USSecStorageSuffix)]
        public void GetStorageSuffix_Returns_Suffix_Based_On_CloudType(string cloudNameSetting, string suffix)
        {
            var testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.CloudName, cloudNameSetting);
            Assert.Equal(suffix, testEnvironment.GetStorageSuffix());
        }
    }
}
