// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests
{
    public class ConfigurationUtilityTests
    {
        [Fact]
        public void GetSettingFromConfigOrEnvironment_NotFound_ReturnsEmpty()
        {
            string value = ConfigurationUtility.GetSettingFromConfigOrEnvironment("DNE");
            Assert.Equal(null, value);
        }

        [Fact]
        public void GetSettingFromConfigOrEnvironment_ConfigSetting_NoEnvironmentSetting()
        {
            string value = ConfigurationUtility.GetSettingFromConfigOrEnvironment("DisableSetting0");
            Assert.Equal("0", value);
        }

        [Fact]
        public void GetSettingFromConfigOrEnvironment_EnvironmentSetting_NoConfigSetting()
        {
            Environment.SetEnvironmentVariable("EnvironmentSetting", "1");

            string value = ConfigurationUtility.GetSettingFromConfigOrEnvironment("EnvironmentSetting");
            Assert.Equal("1", value);

            Environment.SetEnvironmentVariable("EnvironmentSetting", null);
        }

        [Fact]
        public void GetSettingFromConfigOrEnvironment_ConfigAndEnvironment_ConfigWins()
        {
            Environment.SetEnvironmentVariable("DisableSetting0", "1");

            string value = ConfigurationUtility.GetSettingFromConfigOrEnvironment("DisableSetting0");
            Assert.Equal("0", value);

            Environment.SetEnvironmentVariable("EnvironmentSetting", null);
        }
    }
}
