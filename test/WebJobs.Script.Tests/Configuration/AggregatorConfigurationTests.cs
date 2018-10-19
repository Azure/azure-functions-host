// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
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
        private static readonly string AggregatorPath = ConfigurationPath.Combine(ConfigurationSectionNames.JobHost, ConfigurationSectionNames.Aggregator);

        [Fact]
        public void Aggregator_Registered_ByDefault()
        {
            IHost host = new HostBuilder()
                .ConfigureDefaultTestWebScriptHost()
                .Build();

            // Make sure there are two providers registered, and that one has a type with
            // "FunctionResultAggregatorProvider" (since it is internal to WebJobs)
            var eventCollectorProviders = host.Services.GetServices<IEventCollectorProvider>();
            Assert.Equal(2, eventCollectorProviders.Count());
            Assert.Single(eventCollectorProviders.OfType<FunctionInstanceLogCollectorProvider>());
            Assert.Single(eventCollectorProviders.Where(p => p.GetType().Name.Contains("FunctionResultAggregatorProvider")));

            // Also make sure that when requesting the collectors, we end up with a composite that
            // includes both of the collectors above. This prevents any overriding of the IEventCollectorFactory
            var eventLogger = host.Services.GetServices<IAsyncCollector<FunctionInstanceLogEntry>>().Single();
            var field = eventLogger.GetType().GetField("_collectors", BindingFlags.NonPublic | BindingFlags.Instance);
            var collectors = (IEnumerable<IAsyncCollector<FunctionInstanceLogEntry>>)field.GetValue(eventLogger);
            Assert.Equal(2, collectors.Count());
            Assert.Single(collectors.OfType<FunctionInstanceLogger>());
            Assert.Single(collectors.Where(p => p.GetType().Name.Contains("FunctionResultAggregator")));
        }

        [Fact]
        public void Configuration_BindsTo_AggregatorOptions()
        {
            IHost host = new HostBuilder()
                .ConfigureAppConfiguration(c =>
                {
                    c.AddInMemoryCollection(new Dictionary<string, string>
                    {
                        { ConfigurationPath.Combine(AggregatorPath, "BatchSize"), "33" },
                        { ConfigurationPath.Combine(AggregatorPath, "FlushTimeout"), "00:00:33" }
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
