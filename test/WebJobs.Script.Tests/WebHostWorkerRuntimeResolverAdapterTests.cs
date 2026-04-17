// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Tests.DependencyInjection
{
    public sealed class WebHostWorkerRuntimeResolverAdapterTests
    {
        [Fact]
        public void GetWorkerRuntime_ReturnsConfigurationValue()
        {
            var configuration = CreateConfiguration(EnvironmentSettingNames.FunctionWorkerRuntime, "python");
            var adapter = new WebHostWorkerRuntimeResolverAdapter(configuration);

            var result = adapter.GetWorkerRuntime();

            Assert.Equal("python", result);
        }

        [Fact]
        public void GetWorkerRuntime_AlwaysReadsFromConfiguration()
        {
            var configuration = new ReloadableTestConfiguration();
            configuration.Set(EnvironmentSettingNames.FunctionWorkerRuntime, "node");

            var adapter = new WebHostWorkerRuntimeResolverAdapter(configuration);

            Assert.Equal("node", adapter.GetWorkerRuntime());

            // Update the underlying configuration value
            configuration.Set(EnvironmentSettingNames.FunctionWorkerRuntime, "python");

            Assert.Equal("python", adapter.GetWorkerRuntime());
        }

        [Fact]
        public void GetWorkerRuntime_DefaultValue_Returned_WhenConfigurationMissing()
        {
            var configuration = CreateConfiguration();
            var adapter = new WebHostWorkerRuntimeResolverAdapter(configuration);

            var result = adapter.GetWorkerRuntime(defaultValue: "fallback");

            Assert.Equal("fallback", result);
        }

        [Fact]
        public void GetWorkerRuntime_ReturnsNull_WhenConfigurationMissing_AndNoDefault()
        {
            var configuration = CreateConfiguration();
            var adapter = new WebHostWorkerRuntimeResolverAdapter(configuration);

            var result = adapter.GetWorkerRuntime();

            Assert.Null(result);
        }

        [Fact]
        public async Task GetWorkerRuntime_Concurrency_ReturnsConsistentResults()
        {
            var configuration = CreateConfiguration(EnvironmentSettingNames.FunctionWorkerRuntime, "python");
            var adapter = new WebHostWorkerRuntimeResolverAdapter(configuration);

            const int taskCount = 10;
            var tasks = new Task<string>[taskCount];
            for (int i = 0; i < taskCount; i++)
            {
                tasks[i] = Task.Run(() => adapter.GetWorkerRuntime());
            }

            var results = await Task.WhenAll(tasks);

            Assert.All(results, r => Assert.Equal("python", r));
        }

        [Fact]
        public void Constructor_ThrowsOnNullConfiguration()
        {
            Assert.Throws<ArgumentNullException>(() => new WebHostWorkerRuntimeResolverAdapter(null));
        }

        private static IConfiguration CreateConfiguration(string key = null, string value = null)
        {
            var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (key is not null)
            {
                settings[key] = value;
            }

            return new ConfigurationBuilder()
                .AddInMemoryCollection(settings)
                .Build();
        }

        /// <summary>
        /// A simple IConfiguration wrapper that supports mutable values for testing.
        /// </summary>
        private sealed class ReloadableTestConfiguration : IConfiguration
        {
            private readonly Dictionary<string, string> _data = new(StringComparer.OrdinalIgnoreCase);

            public string this[string key]
            {
                get => _data.TryGetValue(key, out var value) ? value : null;
                set => _data[key] = value;
            }

            public void Set(string key, string value) => _data[key] = value;

            public IEnumerable<IConfigurationSection> GetChildren() => Array.Empty<IConfigurationSection>();

            public IConfigurationSection GetSection(string key) => null;

            public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken() => null;
        }
    }
}
