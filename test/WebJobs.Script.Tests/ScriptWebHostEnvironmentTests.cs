// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.WebHost;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class ScriptWebHostEnvironmentTests
    {
        [Theory]
        [InlineData("1", true)]
        [InlineData(null, false)]
        [InlineData("0", false)]
        public void InStandbyMode_FirstObservationCapturesExpectedValue(
            string initialValue,
            bool expectedInitialValue)
        {
            var environment = new Tests.TestEnvironment();
            if (initialValue is not null)
            {
                environment.SetEnvironmentVariable(
                    EnvironmentSettingNames.AzureWebsitePlaceholderMode,
                    initialValue);
            }

            var scriptHostEnvironment = new ScriptWebHostEnvironment(environment);

            Assert.Equal(expectedInitialValue, scriptHostEnvironment.InStandbyMode);
            Assert.Equal(expectedInitialValue, scriptHostEnvironment.InStandbyMode);

            if (expectedInitialValue)
            {
                environment.SetEnvironmentVariable(
                    EnvironmentSettingNames.AzureWebsitePlaceholderMode,
                    "0");
                Assert.False(scriptHostEnvironment.InStandbyMode);
            }

            environment.SetEnvironmentVariable(
                EnvironmentSettingNames.AzureWebsitePlaceholderMode,
                "1");
            Assert.False(scriptHostEnvironment.InStandbyMode);
        }
    }
}
