// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class AggregatorConfigurationTests
    {
        [Fact]
        public void Configuration_BindsTo_AggregatorOptions()
        {
            string aggregatorPath = ConfigurationPath.Combine(ConfigurationSectionNames.JobHost, ConfigurationSectionNames.Aggregator);
            IHost host = new HostBuilder()
                .ConfigureAppConfiguration(c =>
                {
                    c.AddInMemoryCollection(new Dictionary<string, string>
                    {
                        { ConfigurationPath.Combine(aggregatorPath, "BatchSize"), "33" },
                        { ConfigurationPath.Combine(aggregatorPath, "FlushTimeout"), "00:00:33" }
                    });
                })
                .ConfigureDefaultTestWebScriptHost()
                .Build();

            var options = host.Services.GetService<IOptions<FunctionResultAggregatorOptions>>().Value;
            Assert.Equal(33, options.BatchSize);
            Assert.Equal(TimeSpan.FromSeconds(33), options.FlushTimeout);
        }
    }
}
