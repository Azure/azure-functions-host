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
