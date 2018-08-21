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

            var webEnvironment = CreateEnvironment(new TestEnvironment(settings));

            ScriptApplicationHostOptionsSetup setup = CreateSetupWithConfiguration(webEnvironment);

            var options = new ScriptApplicationHostOptions();
            setup.Configure(options);

            Assert.EndsWith(@"Functions\Standby\Logs", options.LogPath);
            Assert.EndsWith(@"Functions\Standby\WWWRoot", options.ScriptPath);
            Assert.EndsWith(@"Functions\Standby\Secrets", options.SecretsPath);
            Assert.False(options.IsSelfHost);
        }

        private ScriptApplicationHostOptionsSetup CreateSetupWithConfiguration(IScriptWebHostEnvironment environment = null)
        {
            var builder = new ConfigurationBuilder();
            environment = environment ?? CreateEnvironment(SystemEnvironment.Instance);

            var configuration = builder.Build();

            return new ScriptApplicationHostOptionsSetup(configuration, environment);
        }

        private IScriptWebHostEnvironment CreateEnvironment(IEnvironment environment)
        {
            environment = environment ?? SystemEnvironment.Instance;

            return new ScriptWebHostEnvironment(environment);
        }
    }
}
