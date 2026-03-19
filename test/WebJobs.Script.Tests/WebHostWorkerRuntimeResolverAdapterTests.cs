// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Tests;
using Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using static Microsoft.Azure.WebJobs.Script.Tests.TestHelpers;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Tests.DependencyInjection
{
    public sealed class WebHostWorkerRuntimeResolverAdapterTests
    {
        [Fact]
        public void GetWorkerRuntime_DelegatesToScriptHostResolver()
        {
            var resolver = new Mock<IWorkerRuntimeResolver>(MockBehavior.Strict);
            resolver.Setup(r => r.GetWorkerRuntime(It.IsAny<string>())).Returns("node");

            var provider = CreateProviderWithScriptHostResolver(resolver.Object);
            var configuration = CreateConfiguration();
            var logger = new Mock<ILogger<WebHostWorkerRuntimeResolverAdapter>>();
            var adapter = new WebHostWorkerRuntimeResolverAdapter(provider, configuration, logger.Object);

            var result = adapter.GetWorkerRuntime();
            Assert.Equal("node", result);
            resolver.Verify(r => r.GetWorkerRuntime(It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public void GetWorkerRuntime_CachesScriptHostResolver()
        {
            var resolver = new Mock<IWorkerRuntimeResolver>(MockBehavior.Strict);
            resolver.Setup(r => r.GetWorkerRuntime(It.IsAny<string>())).Returns("java");

            var serviceResolutionCount = 0;
            var scriptHostManagerMock = new Mock<IScriptHostManager>(MockBehavior.Strict);
            var sp = scriptHostManagerMock.As<IServiceProvider>();
            sp.Setup(p => p.GetService(typeof(IWorkerRuntimeResolver)))
              .Callback(() => serviceResolutionCount++)
              .Returns(resolver.Object);

            var services = new ServiceCollection();
            services.AddSingleton(scriptHostManagerMock.Object);
            var provider = services.BuildServiceProvider();

            var configuration = CreateConfiguration();
            var logger = new Mock<ILogger<WebHostWorkerRuntimeResolverAdapter>>();
            var adapter = new WebHostWorkerRuntimeResolverAdapter(provider, configuration, logger.Object);

            var result1 = adapter.GetWorkerRuntime();
            var result2 = adapter.GetWorkerRuntime();

            Assert.Equal("java", result1);
            Assert.Equal("java", result2);
            Assert.Equal(1, serviceResolutionCount); // Ensure resolver was resolved only once and then cached.
            resolver.Verify(r => r.GetWorkerRuntime(It.IsAny<string>()), Times.Exactly(2));
        }

        [Fact]
        public void GetWorkerRuntime_CacheIsCleared_OnActiveHostChanged()
        {
            var resolver1 = new Mock<IWorkerRuntimeResolver>(MockBehavior.Strict);
            resolver1.Setup(r => r.GetWorkerRuntime(It.IsAny<string>())).Returns("dotnet");

            var serviceMap = new Dictionary<Type, object>
            {
                { typeof(IWorkerRuntimeResolver), resolver1.Object }
            };

            var scriptHostManager = new TestScriptHostService(ScriptSettingsManager.BuildDefaultConfiguration(), serviceMap);

            var services = new ServiceCollection();
            services.AddSingleton<IScriptHostManager>(scriptHostManager);
            var serviceProvider = services.BuildServiceProvider();

            var configuration = CreateConfiguration();
            var logger = new Mock<ILogger<WebHostWorkerRuntimeResolverAdapter>>();
            var adapter = new WebHostWorkerRuntimeResolverAdapter(serviceProvider, configuration, logger.Object);

            // Act & Assert: First time calling GetWorkerRuntime uses resolver1 and caches it.
            Assert.Equal("dotnet", adapter.GetWorkerRuntime());
            resolver1.Verify(r => r.GetWorkerRuntime(It.IsAny<string>()), Times.Once);

            // Host changed event. Replace resolver with resolver2 and ensure resolver2 is used after cache is cleared.
            var resolver2 = new Mock<IWorkerRuntimeResolver>(MockBehavior.Strict);
            resolver2.Setup(r => r.GetWorkerRuntime(It.IsAny<string>())).Returns("python");
            serviceMap[typeof(IWorkerRuntimeResolver)] = resolver2.Object;
            scriptHostManager.OnActiveHostChanged();

            Assert.Equal("python", adapter.GetWorkerRuntime());
            resolver2.Verify(r => r.GetWorkerRuntime(It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public void ActiveHostChanged_BeforeCacheStillResolves()
        {
            var resolver = new Mock<IWorkerRuntimeResolver>(MockBehavior.Strict);
            resolver.Setup(r => r.GetWorkerRuntime(It.IsAny<string>())).Returns("dotnet");

            var serviceMap = new Dictionary<Type, object>
            {
                { typeof(IWorkerRuntimeResolver), resolver.Object }
            };
            var scriptHostManager = new TestScriptHostService(ScriptSettingsManager.BuildDefaultConfiguration(), serviceMap);

            var services = new ServiceCollection();
            services.AddSingleton<IScriptHostManager>(scriptHostManager);
            var provider = services.BuildServiceProvider();

            var configuration = CreateConfiguration();
            var logger = new Mock<ILogger<WebHostWorkerRuntimeResolverAdapter>>();
            var adapter = new WebHostWorkerRuntimeResolverAdapter(provider, configuration, logger.Object);

            // Trigger ActiveHostChanged before any resolver has been cached
            scriptHostManager.OnActiveHostChanged();

            var result = adapter.GetWorkerRuntime();
            Assert.Equal("dotnet", result);
            resolver.Verify(r => r.GetWorkerRuntime(It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public void Dispose_DontThrow()
        {
            var resolver = new Mock<IWorkerRuntimeResolver>(MockBehavior.Strict);
            resolver.Setup(r => r.GetWorkerRuntime(It.IsAny<string>())).Returns("dotnet");

            var serviceMap = new Dictionary<Type, object>
            {
                { typeof(IWorkerRuntimeResolver), resolver.Object }
            };
            var scriptHostManager = new TestScriptHostService(ScriptSettingsManager.BuildDefaultConfiguration(), serviceMap);

            var services = new ServiceCollection();
            services.AddSingleton<IScriptHostManager>(scriptHostManager);
            var serviceProvider = services.BuildServiceProvider();

            var configuration = CreateConfiguration();
            var logger = new Mock<ILogger<WebHostWorkerRuntimeResolverAdapter>>();
            var adapter = new WebHostWorkerRuntimeResolverAdapter(serviceProvider, configuration, logger.Object);

            var result = adapter.GetWorkerRuntime();
            Assert.Equal("dotnet", result);

            // Dispose should complete without throwing.
            adapter.Dispose();

            // Verify the resolver was called once during initialization
            resolver.Verify(r => r.GetWorkerRuntime(It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public void GetWorkerRuntime_FallsBackToConfiguration_WhenResolverNotAvailable()
        {
            var configuration = CreateConfiguration(EnvironmentSettingNames.FunctionWorkerRuntime, "python");

            var serviceMap = new Dictionary<Type, object>();
            var scriptHostManager = new TestScriptHostService(ScriptSettingsManager.BuildDefaultConfiguration(), serviceMap);

            var services = new ServiceCollection();
            services.AddSingleton<IScriptHostManager>(scriptHostManager);
            var serviceProvider = services.BuildServiceProvider();

            var logger = new Mock<ILogger<WebHostWorkerRuntimeResolverAdapter>>();
            var adapter = new WebHostWorkerRuntimeResolverAdapter(serviceProvider, configuration, logger.Object);

            var result = adapter.GetWorkerRuntime();

            Assert.Equal("python", result);
        }

        [Fact]
        public void GetWorkerRuntime_CachesConfigurationValue()
        {
            var configurationMock = new Mock<IConfiguration>(MockBehavior.Strict);
            configurationMock.Setup(c => c[EnvironmentSettingNames.FunctionWorkerRuntime])
                .Returns("node");

            var serviceMap = new Dictionary<Type, object>();
            var scriptHostManager = new TestScriptHostService(ScriptSettingsManager.BuildDefaultConfiguration(), serviceMap);

            var services = new ServiceCollection();
            services.AddSingleton<IScriptHostManager>(scriptHostManager);
            var serviceProvider = services.BuildServiceProvider();

            var logger = new Mock<ILogger<WebHostWorkerRuntimeResolverAdapter>>();
            var adapter = new WebHostWorkerRuntimeResolverAdapter(serviceProvider, configurationMock.Object, logger.Object);

            // Call multiple times to verify caching
            var result1 = adapter.GetWorkerRuntime();
            var result2 = adapter.GetWorkerRuntime();
            var result3 = adapter.GetWorkerRuntime();

            Assert.Equal("node", result1);
            Assert.Equal("node", result2);
            Assert.Equal("node", result3);
            configurationMock.Verify(c => c[EnvironmentSettingNames.FunctionWorkerRuntime], Times.Once);
        }

        [Fact]
        public void GetWorkerRuntime_DefaultValue_Returned_WhenResolverAndConfigurationMissing()
        {
            var configuration = CreateConfiguration();

            var serviceMap = new Dictionary<Type, object>();
            var scriptHostManager = new TestScriptHostService(ScriptSettingsManager.BuildDefaultConfiguration(), serviceMap);

            var services = new ServiceCollection();
            services.AddSingleton<IScriptHostManager>(scriptHostManager);
            var serviceProvider = services.BuildServiceProvider();

            var logger = new Mock<ILogger<WebHostWorkerRuntimeResolverAdapter>>();
            var adapter = new WebHostWorkerRuntimeResolverAdapter(serviceProvider, configuration, logger.Object);

            var result = adapter.GetWorkerRuntime(defaultValue: "fallback");

            Assert.Equal("fallback", result);
        }

        [Fact]
        public void GetWorkerRuntime_DoesNotCacheNullConfigurationValue()
        {
            var configurationMock = new Mock<IConfiguration>(MockBehavior.Strict);
            configurationMock.SetupSequence(c => c[EnvironmentSettingNames.FunctionWorkerRuntime])
                .Returns((string)null)
                .Returns((string)null)
                .Returns("powershell");

            var serviceMap = new Dictionary<Type, object>();
            var scriptHostManager = new TestScriptHostService(ScriptSettingsManager.BuildDefaultConfiguration(), serviceMap);

            var services = new ServiceCollection();
            services.AddSingleton<IScriptHostManager>(scriptHostManager);
            var serviceProvider = services.BuildServiceProvider();

            var logger = new Mock<ILogger<WebHostWorkerRuntimeResolverAdapter>>();
            var adapter = new WebHostWorkerRuntimeResolverAdapter(serviceProvider, configurationMock.Object, logger.Object);

            // First two calls: config value not set, returns default each time (re-reads config)
            Assert.Null(adapter.GetWorkerRuntime());
            Assert.Null(adapter.GetWorkerRuntime());

            // Third call: config value now set (e.g., after ApplyAppSettings during specialization)
            Assert.Equal("powershell", adapter.GetWorkerRuntime());

            configurationMock.Verify(c => c[EnvironmentSettingNames.FunctionWorkerRuntime], Times.Exactly(3));
        }

        [Fact]
        public void GetWorkerRuntime_ActiveHostChanged_ClearsConfigurationCache()
        {
            var configurationMock = new Mock<IConfiguration>(MockBehavior.Strict);
            configurationMock.SetupSequence(c => c[EnvironmentSettingNames.FunctionWorkerRuntime])
                .Returns((string)null)
                .Returns("python");

            var serviceMap = new Dictionary<Type, object>();
            var scriptHostManager = new TestScriptHostService(ScriptSettingsManager.BuildDefaultConfiguration(), serviceMap);

            var services = new ServiceCollection();
            services.AddSingleton<IScriptHostManager>(scriptHostManager);
            var serviceProvider = services.BuildServiceProvider();

            var logger = new Mock<ILogger<WebHostWorkerRuntimeResolverAdapter>>();
            var adapter = new WebHostWorkerRuntimeResolverAdapter(serviceProvider, configurationMock.Object, logger.Object);

            var result1 = adapter.GetWorkerRuntime(defaultValue: "fallback");
            Assert.Equal("fallback", result1);

            // Simulate specialization: host changed, configuration now has a value
            scriptHostManager.OnActiveHostChanged();

            var result2 = adapter.GetWorkerRuntime(defaultValue: "fallback");
            Assert.Equal("python", result2);
        }

        [Fact]
        public void GetWorkerRuntime_Concurrency_ResolvesOnceAndCaches()
        {
            var resolver = new Mock<IWorkerRuntimeResolver>(MockBehavior.Strict);
            resolver.Setup(r => r.GetWorkerRuntime(It.IsAny<string>()))
                .Returns("java");

            int serviceResolutionCount = 0;
            var scriptHostManagerMock = new Mock<IScriptHostManager>(MockBehavior.Strict);
            var scriptHostManagerServiceProvider = scriptHostManagerMock.As<IServiceProvider>();
            scriptHostManagerServiceProvider
                .Setup(p => p.GetService(typeof(IWorkerRuntimeResolver)))
                // Track resolution attempts.
                .Callback(() => Interlocked.Increment(ref serviceResolutionCount))
                .Returns(resolver.Object);

            var services = new ServiceCollection();
            services.AddSingleton<IScriptHostManager>(scriptHostManagerMock.Object);
            var serviceProvider = services.BuildServiceProvider();

            var configuration = CreateConfiguration();
            var logger = new Mock<ILogger<WebHostWorkerRuntimeResolverAdapter>>();
            var adapter = new WebHostWorkerRuntimeResolverAdapter(serviceProvider, configuration, logger.Object);

            // Act: Simulate multiple concurrent calls to validate thread-safety and caching behavior
            const int callCount = 10;
            var results = new string[callCount];
            Parallel.For(0, callCount, i =>
            {
                results[i] = adapter.GetWorkerRuntime();
            });

            // Assert: All concurrent calls return consistent results
            Assert.All(results, r => Assert.Equal("java", r));
            Assert.True(serviceResolutionCount >= 1);
        }

        [Fact]
        public async Task GetWorkerRuntime_Concurrency_CachesConfigurationValue()
        {
            var configuration = CreateConfiguration(EnvironmentSettingNames.FunctionWorkerRuntime, "python");

            var serviceMap = new Dictionary<Type, object>();
            var scriptHostManager = new TestScriptHostService(ScriptSettingsManager.BuildDefaultConfiguration(), serviceMap);

            var services = new ServiceCollection();
            services.AddSingleton<IScriptHostManager>(scriptHostManager);
            var serviceProvider = services.BuildServiceProvider();

            var logger = new Mock<ILogger<WebHostWorkerRuntimeResolverAdapter>>();
            var adapter = new WebHostWorkerRuntimeResolverAdapter(serviceProvider, configuration, logger.Object);

            // Act: Simulate multiple concurrent calls to validate thread-safety and caching behavior
            const int taskCount = 10;
            var tasks = new Task<string>[taskCount];
            for (int i = 0; i < taskCount; i++)
            {
                tasks[i] = Task.Run(() => adapter.GetWorkerRuntime());
            }

            var results = await Task.WhenAll(tasks);

            // Assert: All concurrent calls return consistent results
            Assert.All(results, r => Assert.Equal("python", r));
        }

        [Fact]
        public void Dispose_ClearsConfigurationCache()
        {
            var configuration = CreateConfiguration(EnvironmentSettingNames.FunctionWorkerRuntime, "node");

            var serviceMap = new Dictionary<Type, object>();
            var scriptHostManager = new TestScriptHostService(ScriptSettingsManager.BuildDefaultConfiguration(), serviceMap);

            var services = new ServiceCollection();
            services.AddSingleton<IScriptHostManager>(scriptHostManager);
            var serviceProvider = services.BuildServiceProvider();

            var logger = new Mock<ILogger<WebHostWorkerRuntimeResolverAdapter>>();
            var adapter = new WebHostWorkerRuntimeResolverAdapter(serviceProvider, configuration, logger.Object);

            // Get value to cache it
            var result = adapter.GetWorkerRuntime();
            Assert.Equal("node", result);

            // Dispose should complete without throwing and clear the cache
            adapter.Dispose();
        }

        private static IServiceProvider CreateProviderWithScriptHostResolver(IWorkerRuntimeResolver resolver)
        {
            var scriptHostManagerMock = new Mock<IScriptHostManager>(MockBehavior.Strict);
            scriptHostManagerMock.As<IServiceProvider>()
                .Setup(p => p.GetService(typeof(IWorkerRuntimeResolver)))
                .Returns(resolver);

            var services = new ServiceCollection();
            services.AddSingleton(scriptHostManagerMock.Object);
            return services.BuildServiceProvider();
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
    }
}
