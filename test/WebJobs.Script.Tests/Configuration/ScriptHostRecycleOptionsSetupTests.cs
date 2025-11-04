// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using AwesomeAssertions;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Rpc.Configuration;
using Microsoft.Azure.WebJobs.Script.Workers.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class ScriptHostRecycleOptionsSetupTests
    {
        [Theory]
        [InlineData("true", true, true)]
        [InlineData("false", false, false)]
        [InlineData("false", true, true)]
        [InlineData("true", false, true)]
        public void Configure_SequentialHostRestartRequired_Works(string configValue, bool isPortManuallySet, bool output)
        {
            var httpWorkerOptions = new HttpWorkerOptions();
            typeof(HttpWorkerOptions)
                .GetProperty(nameof(HttpWorkerOptions.IsPortManuallySet))!
                .SetValue(httpWorkerOptions, isPortManuallySet);

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(
                [
                    new KeyValuePair<string, string>(ConfigurationSectionNames.SequentialJobHostRestart, configValue)
                ])
                .Build();

            var setup = new ScriptHostRecycleOptionsSetup(config);

            var options = new ScriptHostRecycleOptions();
            setup.Configure(options);

            var rpcOptionsSetup = new RpcScriptHostRecycleOptionsSetup(new OptionsWrapper<HttpWorkerOptions>(httpWorkerOptions));
            rpcOptionsSetup.Configure(options);

            options.SequentialHostRestartRequired.Should().Be(output);
        }
    }
}
