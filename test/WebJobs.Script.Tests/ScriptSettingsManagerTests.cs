// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class ScriptSettingsManagerTests
    {
        [Theory]
        [InlineData("testsite", "production", "testsite")]
        [InlineData("testsite", "Production", "testsite")]
        [InlineData("testsite", null, "testsite")]
        [InlineData("testsite", "staging", "testsite-staging")]
        [InlineData("testsite", "dev", "testsite-dev")]
        [InlineData("TestSite", "Dev", "testsite-dev")]
        public void UniqueSlotName_ReturnsExpectedValue(string siteName, string slotName, string expectedValue)
        {
            var settingsManager = ScriptSettingsManager.Instance;

            var variables = new Dictionary<string, string>
            {
                { EnvironmentSettingNames.AzureWebsiteName, siteName },
                { EnvironmentSettingNames.AzureWebsiteSlotName, slotName },
            };

            using (var tempVariables = new TestScopedEnvironmentVariable(variables))
            {
                Assert.Equal(expectedValue, ScriptSettingsManager.Instance.AzureWebsiteUniqueSlotName);
            }
        }

        [Fact]
        public void SettingsAreNotCached()
        {
            using (var variable = new TestScopedEnvironmentVariable(nameof(SettingsAreNotCached), "foo"))
            {
                Assert.Equal("foo", ScriptSettingsManager.Instance.GetSetting(nameof(SettingsAreNotCached)));

                Environment.SetEnvironmentVariable(nameof(SettingsAreNotCached), "bar");
                Assert.Equal("bar", ScriptSettingsManager.Instance.GetSetting(nameof(SettingsAreNotCached)));
            }
        }
    }
}
