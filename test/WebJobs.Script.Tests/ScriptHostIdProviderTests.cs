// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class ScriptHostIdProviderTests
    {
        [Fact]
        public async Task GetHostIdAsync_WithConfigurationHostId_ReturnsConfigurationHostId()
        {
            var options = new ScriptApplicationHostOptions();
            var environment = new TestEnvironment();

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    { ConfigurationPath.Combine(ConfigurationSectionNames.HostIdPath), "test-host-id" }
                })
                .Build();

            var provider = new ScriptHostIdProvider(config, environment, new TestOptionsMonitor<ScriptApplicationHostOptions>(options));

            string hostId = await provider.GetHostIdAsync(CancellationToken.None);

            Assert.Equal("test-host-id", hostId);
        }

        [Theory]
        [InlineData("TEST-FUNCTIONS--", "123123", "test-functions")]
        [InlineData("TEST-FUNCTIONS-XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX", "123123", "test-functions-xxxxxxxxxxxxxxxxx")]
        [InlineData("TEST-FUNCTIONS-XXXXXXXXXXXXXXXX-XXXX", "123123", "test-functions-xxxxxxxxxxxxxxxx")] /* 32nd character is a '-' */
        [InlineData("TEST-FUNCTIONS-XXXXXXXXXXXXXXXX-XXXX", null, "testsite")] // Linux consumption scenario where host id will be derived from the host name.
        [InlineData(null, "123123", null)]
        public void GetDefaultHostId_AzureHost_ReturnsExpectedResult(string siteName, string azureWebsiteInstanceId, string expected)
        {
            var options = new ScriptApplicationHostOptions
            {
                ScriptPath = @"c:\testscripts"
            };

            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteInstanceId, azureWebsiteInstanceId);
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName, siteName);
            environment.SetEnvironmentVariable(EnvironmentSettingNames.ContainerName, "testContainer");
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHostName, "testsite.azurewebsites.net");
            string hostId = ScriptHostIdProvider.GetDefaultHostId(environment, options);
            Assert.Equal(expected, hostId);
        }

        [Fact]
        public void GetDefaultHostId_SelfHost_ReturnsExpectedResult()
        {
            var options = new ScriptApplicationHostOptions
            {
                IsSelfHost = true,
                ScriptPath = @"c:\testing\FUNCTIONS-TEST\test$#"
            };

            var environmentMock = new Mock<IEnvironment>();

            string hostId = ScriptHostIdProvider.GetDefaultHostId(environmentMock.Object, options);

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
