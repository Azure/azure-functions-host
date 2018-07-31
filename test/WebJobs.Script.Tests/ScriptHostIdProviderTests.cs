// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Text;
using Microsoft.Azure.WebJobs.Script.Config;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class ScriptHostIdProviderTests
    {
        [Theory]
        [InlineData("TEST-FUNCTIONS--", "test-functions")]
        [InlineData("TEST-FUNCTIONS-XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX", "test-functions-xxxxxxxxxxxxxxxxx")]
        [InlineData("TEST-FUNCTIONS-XXXXXXXXXXXXXXXX-XXXX", "test-functions-xxxxxxxxxxxxxxxx")] /* 32nd character is a '-' */
        [InlineData(null, null)]
        public void GetDefaultHostId_AzureHost_ReturnsExpectedResult(string input, string expected)
        {
            var config = new ScriptHostOptions();
            var scriptSettingsManagerMock = new Mock<ScriptSettingsManager>(MockBehavior.Strict, null);
            scriptSettingsManagerMock.SetupGet(p => p.AzureWebsiteUniqueSlotName).Returns(() => input);

            string hostId = ScriptHostIdProvider.GetDefaultHostId(scriptSettingsManagerMock.Object, config);
            Assert.Equal(expected, hostId);
        }

        [Fact]
        public void GetDefaultHostId_SelfHost_ReturnsExpectedResult()
        {
            var config = new ScriptHostOptions
            {
                IsSelfHost = true,
                RootScriptPath = @"c:\testing\FUNCTIONS-TEST\test$#"
            };

            var scriptSettingsManagerMock = new Mock<ScriptSettingsManager>(MockBehavior.Strict, null);

            string hostId = ScriptHostIdProvider.GetDefaultHostId(scriptSettingsManagerMock.Object, config);

            // This suffix is a stable hash code derived from the "RootScriptPath" string passed in the configuration.
            // We're using the literal here as we want this test to fail if this compuation ever returns something different.
            string suffix = "473716271";

            string sanitizedMachineName = Environment.MachineName
                    .Where(char.IsLetterOrDigit)
                    .Aggregate(new StringBuilder(), (b, c) => b.Append(c)).ToString().ToLowerInvariant();
            Assert.Equal($"{sanitizedMachineName}-{suffix}", hostId);
        }
    }
}
