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

        [Theory]
        [InlineData("Azure", CloudConstants.AzureVaultSuffix)]
        [InlineData("azuRe", CloudConstants.AzureVaultSuffix)]
        [InlineData("", CloudConstants.AzureVaultSuffix)]
        [InlineData(null, CloudConstants.AzureVaultSuffix)]
        [InlineData("Blackforest", CloudConstants.BlackforestVaultSuffix)]
        [InlineData("Fairfax", CloudConstants.FairfaxVaultSuffix)]
        [InlineData("Mooncake", CloudConstants.MooncakeVaultSuffix)]
        public void GetVaultSuffix_Returns_Suffix_Based_On_CloudType(string cloudNameSetting, string suffix)
        {
            var testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.CloudName, cloudNameSetting);
            Assert.Equal(suffix, testEnvironment.GetVaultSuffix());
        }

        [Theory]
        [InlineData(ScriptConstants.DynamicSku, true)]
        [InlineData("test", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void Returns_IsWindowsConsumption(string websiteSku, bool isWindowsElasticPremium)
        {
            var testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSku, websiteSku);
            Assert.Equal(isWindowsElasticPremium, testEnvironment.IsWindowsConsumption());
            Assert.Equal(isWindowsElasticPremium, testEnvironment.IsConsumptionSku());
            Assert.Equal(isWindowsElasticPremium, testEnvironment.IsDynamicSku());
        }

        [Theory]
        [InlineData("website-instance-id", "container-name", "1", false, false)]
        [InlineData("website-instance-id", "container-name", "", false, false)]
        [InlineData("website-instance-id", "", "", false, false)]
        [InlineData("", "container-name", "1", false, true)]
        [InlineData("", "container-name", "", true, false)]
        [InlineData("", "container-name", "a", false, true)]
        [InlineData(null, "container-name", "", true, false)]
        [InlineData("", "", "", false, false)]
        [InlineData(null, "", null, false, false)]
        [InlineData("", null, null, false, false)]
        [InlineData(null, null, null,  false, false)]
        public void Returns_IsLinuxConsumption(string websiteInstanceId, string containerName, string legionServiceHost, bool isLinuxConsumptionOnAtlas, bool isLinuxConsumptionOnLegion)
        {
            var testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteInstanceId, websiteInstanceId);
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.ContainerName, containerName);
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.LegionServiceHost, legionServiceHost);
            Assert.Equal(isLinuxConsumptionOnAtlas || isLinuxConsumptionOnLegion, testEnvironment.IsAnyLinuxConsumption());
            Assert.Equal(isLinuxConsumptionOnAtlas, testEnvironment.IsLinuxConsumptionOnAtlas());
            Assert.Equal(isLinuxConsumptionOnLegion, testEnvironment.IsFlexConsumptionSku());
            Assert.Equal(isLinuxConsumptionOnAtlas || isLinuxConsumptionOnLegion, testEnvironment.IsConsumptionSku());
            Assert.Equal(isLinuxConsumptionOnAtlas || isLinuxConsumptionOnLegion, testEnvironment.IsDynamicSku());
            Assert.False(isLinuxConsumptionOnAtlas ? isLinuxConsumptionOnLegion : isLinuxConsumptionOnAtlas);
        }

        [Theory]
        [InlineData(ScriptConstants.ElasticPremiumSku, true)]
        [InlineData("test", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void Returns_IsWindowsElasticPremium(string websiteSku, bool isWindowsElasticPremium)
        {
            var testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSku, websiteSku);
            Assert.Equal(isWindowsElasticPremium, testEnvironment.IsWindowsElasticPremium());
            Assert.Equal(isWindowsElasticPremium, testEnvironment.IsDynamicSku());
            Assert.False(testEnvironment.IsConsumptionSku());
        }
    }
}
