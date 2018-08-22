// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class ScriptApplicationHostOptionsSetupTests
    {
        [Fact]
        public void Configure_InStandbyMode_ReturnsExpectedConfiguration()
        {
            var settings = new Dictionary<string, string>
            {
                { EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1" }
            };

            ScriptApplicationHostOptionsSetup setup = CreateSetupWithConfiguration(new TestEnvironment(settings));

            var options = new ScriptApplicationHostOptions();
            setup.Configure(options);

            Assert.EndsWith(@"functions\standby\logs", options.LogPath);
            Assert.EndsWith(@"functions\standby\wwwroot", options.ScriptPath);
            Assert.EndsWith(@"functions\standby\secrets", options.SecretsPath);
            Assert.False(options.IsSelfHost);
        }

        private ScriptApplicationHostOptionsSetup CreateSetupWithConfiguration(IEnvironment environment = null)
        {
            var builder = new ConfigurationBuilder();
            environment = environment ?? SystemEnvironment.Instance;

            var configuration = builder.Build();

            return new ScriptApplicationHostOptionsSetup(configuration, environment);
        }
    }
}
