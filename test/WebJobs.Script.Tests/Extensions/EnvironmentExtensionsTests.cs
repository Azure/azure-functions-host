// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Extensions
{
    public class EnvironmentExtensionsTests
    {
        [Fact]
        public void GetEffectiveCoresCount_RetrunsExpectedResult()
        {
            TestEnvironment env = new TestEnvironment();
            Assert.Equal(Environment.ProcessorCount, EnvironmentExtensions.GetEffectiveCoresCount(env));

            env.Clear();
            env.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSku, ScriptConstants.DynamicSku);
            Assert.Equal(1, EnvironmentExtensions.GetEffectiveCoresCount(env));

            env.Clear();
            env.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSku, ScriptConstants.DynamicSku);
            env.SetEnvironmentVariable(EnvironmentSettingNames.RoleInstanceId, "dw0SmallDedicatedWebWorkerRole_hr0HostRole-0-VM-1");
            Assert.Equal(Environment.ProcessorCount, EnvironmentExtensions.GetEffectiveCoresCount(env));
        }

        [Theory]
        [InlineData("dw0SmallDedicatedWebWorkerRole_hr0HostRole-0-VM-1", true)]
        [InlineData(null, false)]
        [InlineData("", false)]
        public void IsVMSS_RetrunsExpectedResult(string roleInstanceId, bool expected)
        {
            IEnvironment env = new TestEnvironment();
            if (roleInstanceId != null)
            {
                env.SetEnvironmentVariable(EnvironmentSettingNames.RoleInstanceId, roleInstanceId);
            }

            Assert.Equal(expected, EnvironmentExtensions.IsVMSS(env));
        }

        [Theory]
        [InlineData("RD281878FCB8E7", "RD281878FCB8E7")]
        [InlineData("", "")]
        [InlineData(null, "")]
        public void GetAntaresComputerName_ReturnsExpectedResult(string computerName, string expectedComputerName)
        {
            IEnvironment env = new TestEnvironment();
            if (!string.IsNullOrEmpty(expectedComputerName))
            {
                env.SetEnvironmentVariable(EnvironmentSettingNames.AntaresComputerName, computerName);
            }

            Assert.Equal(expectedComputerName, env.GetAntaresComputerName());
        }

        [Theory]
        [InlineData(true, "RandomContainerName", "", "RandomContainerName")]
        [InlineData(true, "RandomContainerName", "e777fde04dea4eb931d5e5f06e65b4fdf5b375aed60af41dd7b491cf5792e01b", "RandomContainerName")]
        [InlineData(true, "", "", "")]
        [InlineData(true, null, "", "")]
        [InlineData(false, "RandomContainerName", "", "")]
        [InlineData(false, "RandomContainerName", "e777fde04dea4eb931d5e5f06e65b4fdf5b375aed60af41dd7b491cf5792e01b", "e777fde04dea4eb931d5e5f06e65b4fdf5b375aed60af41dd7b491cf5792e01b")]
        [InlineData(false, "", "", "")]
        [InlineData(false, "", null, "")]
        public void GetInstanceId_ReturnsExpectedResult(bool isLinuxConsumption, string containerName, string websiteInstanceId, string expectedValue)
        {
            IEnvironment env = new TestEnvironment();
            if (isLinuxConsumption)
            {
                if (!string.IsNullOrEmpty(containerName))
                {
                    env.SetEnvironmentVariable(EnvironmentSettingNames.ContainerName, containerName);
                }
            }
            else if (!string.IsNullOrEmpty(websiteInstanceId))
            {
                env.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteInstanceId, websiteInstanceId);
            }

            Assert.Equal(expectedValue, env.GetInstanceId());
        }

        [Theory]
        [InlineData(true, false, "89.0.7.73", "89.0.7.74", "89.0.7.73")]
        [InlineData(true, false, "", "89.0.7.74", "")]
        [InlineData(true, false, null, "89.0.7.74", "")]
        [InlineData(true, true, "89.0.7.73", "89.0.7.74", "89.0.7.73")]
        [InlineData(true, true, "", "89.0.7.74", "")]
        [InlineData(true, true, null, "89.0.7.74", "")]
        [InlineData(false, true, "89.0.7.73", "89.0.7.74", "89.0.7.73")]
        [InlineData(false, true, "", "89.0.7.74", "")]
        [InlineData(false, true, null, "89.0.7.74", "")]
        [InlineData(false, false, "89.0.7.73", "89.0.7.74", "89.0.7.74")]
        [InlineData(false, false, "89.0.7.73", "", "")]
        [InlineData(false, false, "89.0.7.73", null, "")]
        public void GetAntaresVersion_ReturnsExpectedResult(bool isLinuxConsumption, bool isLinuxAppService, string platformVersionLinux, string platformVersionWindows, string expectedValue)
        {
            IEnvironment env = new TestEnvironment();
            if (isLinuxConsumption)
            {
                env.SetEnvironmentVariable(EnvironmentSettingNames.ContainerName, "RandomContainerName");
            }

            if (isLinuxAppService)
            {
                env.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteInstanceId, "e777fde04dea4eb931d5e5f06e65b4fdf5b375aed60af41dd7b491cf5792e01b");
                env.SetEnvironmentVariable(EnvironmentSettingNames.FunctionsLogsMountPath, "C:\\SomeMountPath");
            }

            if (!string.IsNullOrEmpty(platformVersionLinux))
            {
                env.SetEnvironmentVariable(EnvironmentSettingNames.AntaresPlatformVersionLinux, platformVersionLinux);
            }

            if (!string.IsNullOrEmpty(platformVersionWindows))
            {
                env.SetEnvironmentVariable(EnvironmentSettingNames.AntaresPlatformVersionWindows, platformVersionWindows);
            }

            Assert.Equal(expectedValue, env.GetAntaresVersion());
        }

        [Theory]
        [InlineData("~2", "true", true)]
        [InlineData("~2", "false", true)]
        [InlineData("~2", null, true)]
        [InlineData("~3", "true", true)]
        [InlineData("~3", "false", false)]
        [InlineData("~3", null, false)]
        [InlineData(null, null, false)]
        public void IsV2CompatMode(string extensionVersion, string compatMode, bool expected)
        {
            IEnvironment env = new TestEnvironment();

            if (extensionVersion != null)
            {
                env.SetEnvironmentVariable(EnvironmentSettingNames.FunctionsExtensionVersion, extensionVersion);
            }

            if (compatMode != null)
            {
                env.SetEnvironmentVariable(EnvironmentSettingNames.FunctionsV2CompatibilityModeKey, compatMode);
            }

            Assert.Equal(expected, EnvironmentExtensions.IsV2CompatibilityMode(env));
        }
    }
}
