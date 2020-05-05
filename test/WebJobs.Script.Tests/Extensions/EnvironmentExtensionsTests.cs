// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Extensions
{
    public class EnvironmentExtensionsTests
    {
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
