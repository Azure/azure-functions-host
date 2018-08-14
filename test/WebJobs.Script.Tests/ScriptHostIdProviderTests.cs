// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class ScriptHostIdProviderTests
    {
        [Fact]
        public async Task GetHostIdAsync_WithConfigurationHostId_ReturnsConfigurationHostId()
        {
            var options = new ScriptJobHostOptions();
            var environment = new TestEnvironment();

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    { ConfigurationPath.Combine(ConfigurationSectionNames.JobHost, "id"), "test-host-id" }
                })
                .Build();

            var provider = new ScriptHostIdProvider(config, environment, new OptionsWrapper<ScriptJobHostOptions>(options));

            string hostId = await provider.GetHostIdAsync(CancellationToken.None);

            Assert.Equal("test-host-id", hostId);
        }

        [Theory]
        [InlineData("TEST-FUNCTIONS--", "test-functions")]
        [InlineData("TEST-FUNCTIONS-XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX", "test-functions-xxxxxxxxxxxxxxxxx")]
        [InlineData("TEST-FUNCTIONS-XXXXXXXXXXXXXXXX-XXXX", "test-functions-xxxxxxxxxxxxxxxx")] /* 32nd character is a '-' */
        [InlineData(null, null)]
        public void GetDefaultHostId_AzureHost_ReturnsExpectedResult(string siteName, string expected)
        {
            var config = new ScriptJobHostOptions();
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName, siteName);

            string hostId = ScriptHostIdProvider.GetDefaultHostId(environment, config);
            Assert.Equal(expected, hostId);
        }

        [Fact]
        public void GetDefaultHostId_SelfHost_ReturnsExpectedResult()
        {
            var config = new ScriptJobHostOptions
            {
                IsSelfHost = true,
                RootScriptPath = @"c:\testing\FUNCTIONS-TEST\test$#"
            };

            var environmentMock = new Mock<IEnvironment>();

            string hostId = ScriptHostIdProvider.GetDefaultHostId(environmentMock.Object, config);

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
