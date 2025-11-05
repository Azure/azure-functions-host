// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using AwesomeAssertions;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class ScriptHostRecycleOptionsTests
    {
        [Theory]
        [InlineData("true", true)]
        [InlineData("false", false)]
        [InlineData("", false)]
        public void Configure_SetsSequentialHostRestartRequired_Works(string configValue, bool expected)
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(
                [
                    new KeyValuePair<string, string>(ConfigurationSectionNames.SequentialJobHostRestart, configValue)
                ])
                .Build();

            var options = new ScriptHostRecycleOptions();
            options.Configure(config);

            options.SequentialHostRestartRequired.Should().Be(expected);
        }
    }
}