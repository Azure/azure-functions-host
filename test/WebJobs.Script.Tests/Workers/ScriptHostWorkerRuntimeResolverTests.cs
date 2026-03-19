// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers
{
    public sealed class ScriptHostWorkerRuntimeResolverTests
    {
        [Theory]
        // When ProfileWorkerRuntime is set by a profile, it takes precedence over configuration
        [InlineData("custom", "node", "custom")]
        [InlineData("custom", null, "custom")]

        // When ProfileWorkerRuntime is not set, fall back to the configuration entry
        [InlineData(null, "node", "node")]
        [InlineData("", "node", "node")]
        public void GetWorkerRuntime_UsesProfileWorkerRuntime_BeforeConfiguration(string profileWorkerRuntime, string configWorkerRuntime, string expectedRuntime)
        {
            var configuration = CreateConfiguration(EnvironmentSettingNames.FunctionWorkerRuntime, configWorkerRuntime);
            var scriptJobHostOptions = CreateOptionsMonitor(profileWorkerRuntime);
            var resolver = new ScriptHostWorkerRuntimeResolver(configuration, scriptJobHostOptions);

            var result = resolver.GetWorkerRuntime();

            Assert.Equal(expectedRuntime, result);
        }

        [Fact]
        public void GetWorkerRuntime_NoProfileOrConfiguration_ReturnsDefaultValue()
        {
            var configuration = CreateConfiguration();
            var scriptJobHostOptions = CreateOptionsMonitor(profileWorkerRuntime: null);
            var resolver = new ScriptHostWorkerRuntimeResolver(configuration, scriptJobHostOptions);

            var result = resolver.GetWorkerRuntime("python");

            Assert.Equal("python", result);
        }

        [Fact]
        public void GetWorkerRuntime_NoProfileOrConfigurationOrDefault_ReturnsNullRuntime()
        {
            var configuration = CreateConfiguration();
            var scriptJobHostOptions = CreateOptionsMonitor(profileWorkerRuntime: null);
            var resolver = new ScriptHostWorkerRuntimeResolver(configuration, scriptJobHostOptions);

            var result = resolver.GetWorkerRuntime();

            Assert.Null(result);
        }

        [Fact]
        public void GetWorkerRuntime_CachesResolvedValue()
        {
            var configurationMock = new Mock<IConfiguration>(MockBehavior.Strict);
            configurationMock.Setup(c => c[EnvironmentSettingNames.FunctionWorkerRuntime])
                .Returns("node");

            var scriptJobHostOptions = CreateOptionsMonitor(profileWorkerRuntime: null);
            var resolver = new ScriptHostWorkerRuntimeResolver(configurationMock.Object, scriptJobHostOptions);

            var result1 = resolver.GetWorkerRuntime();
            var result2 = resolver.GetWorkerRuntime();

            Assert.Equal("node", result1);
            Assert.Equal("node", result2);
            configurationMock.Verify(c => c[EnvironmentSettingNames.FunctionWorkerRuntime], Times.Once);
        }

        [Fact]
        public void GetWorkerRuntime_DoesNotCacheDefaultValue()
        {
            var configuration = CreateConfiguration();
            var scriptJobHostOptions = CreateOptionsMonitor(profileWorkerRuntime: null);
            var resolver = new ScriptHostWorkerRuntimeResolver(configuration, scriptJobHostOptions);

            var result1 = resolver.GetWorkerRuntime(defaultValue: string.Empty);
            var result2 = resolver.GetWorkerRuntime();

            Assert.Equal(string.Empty, result1);
            Assert.Null(result2);
        }

        [Fact]
        public void Ctor_ThrowsArgumentNullException_WhenConfigurationIsNull()
        {
            var scriptJobHostOptions = CreateOptionsMonitor(profileWorkerRuntime: null);

            var exception = Assert.Throws<ArgumentNullException>(() =>
                new ScriptHostWorkerRuntimeResolver(null, scriptJobHostOptions));

            Assert.Equal("configuration", exception.ParamName);
        }

        [Fact]
        public void Ctor_ThrowsArgumentNullException_WhenOptionsMonitorIsNull()
        {
            var configuration = CreateConfiguration();

            var exception = Assert.Throws<ArgumentNullException>(() =>
                new ScriptHostWorkerRuntimeResolver(configuration, null));

            Assert.Equal("scriptJobHostOptionsMonitor", exception.ParamName);
        }

        [Theory]
        [InlineData(null, "node", true)] // Configuration lookup
        [InlineData(RpcWorkerConstants.CustomHandlerLanguageWorkerName, null, false)] // Profile worker runtime
        public async Task GetWorkerRuntime_IsThreadSafe_WhenCalledConcurrently(string profileWorkerRuntime, string configWorkerRuntime, bool shouldReadConfiguration)
        {
            var expectedRuntime = profileWorkerRuntime ?? configWorkerRuntime;

            var configuration = shouldReadConfiguration
                ? CreateConfiguration(EnvironmentSettingNames.FunctionWorkerRuntime, configWorkerRuntime)
                : CreateConfiguration();

            var scriptJobHostOptions = CreateOptionsMonitor(profileWorkerRuntime);
            var resolver = new ScriptHostWorkerRuntimeResolver(configuration, scriptJobHostOptions);

            const int taskCount = 10;

            var tasks = new Task<string>[taskCount];
            for (int i = 0; i < taskCount; i++)
            {
                tasks[i] = Task.Run(() => resolver.GetWorkerRuntime());
            }

            var results = await Task.WhenAll(tasks);

            Assert.All(results, result => Assert.Equal(expectedRuntime, result));

            if (shouldReadConfiguration)
            {
                var cachedResult = resolver.GetWorkerRuntime();
                Assert.Equal(expectedRuntime, cachedResult);
            }
        }

        private static IOptionsMonitor<ScriptJobHostOptions> CreateOptionsMonitor(string profileWorkerRuntime)
        {
            var optionsMock = new Mock<IOptionsMonitor<ScriptJobHostOptions>>();
            optionsMock.Setup(o => o.CurrentValue)
                .Returns(new ScriptJobHostOptions { ProfileWorkerRuntime = profileWorkerRuntime });
            return optionsMock.Object;
        }

        private static IConfiguration CreateConfiguration(string key = null, string value = null)
        {
            var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (key is not null && value is not null)
            {
                settings[key] = value;
            }

            return new ConfigurationBuilder()
                .AddInMemoryCollection(settings)
                .Build();
        }
    }
}
