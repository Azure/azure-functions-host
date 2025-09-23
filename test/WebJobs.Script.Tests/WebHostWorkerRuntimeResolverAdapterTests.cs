// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection;
using Microsoft.Azure.WebJobs.Script.Workers;
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
            var logger = new Mock<ILogger<WebHostWorkerRuntimeResolverAdapter>>();
            var adapter = new WebHostWorkerRuntimeResolverAdapter(provider, logger.Object);

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

            var logger = new Mock<ILogger<WebHostWorkerRuntimeResolverAdapter>>();
            var adapter = new WebHostWorkerRuntimeResolverAdapter(provider, logger.Object);

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

            var logger = new Mock<ILogger<WebHostWorkerRuntimeResolverAdapter>>();
            var adapter = new WebHostWorkerRuntimeResolverAdapter(serviceProvider, logger.Object);

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

            var logger = new Mock<ILogger<WebHostWorkerRuntimeResolverAdapter>>();
            var adapter = new WebHostWorkerRuntimeResolverAdapter(provider, logger.Object);

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

            var logger = new Mock<ILogger<WebHostWorkerRuntimeResolverAdapter>>();
            var adapter = new WebHostWorkerRuntimeResolverAdapter(serviceProvider, logger.Object);

            var result = adapter.GetWorkerRuntime();
            Assert.Equal("dotnet", result);

            // Dispose should complete without throwing.
            adapter.Dispose();

            // Verify the resolver was called once during initialization
            resolver.Verify(r => r.GetWorkerRuntime(It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public void GetWorkerRuntime_FallsBackToEnvironment_WhenResolverNotAvailable()
        {
            // Set up environment variable but no script host resolver
            var environmentMock = new Mock<IEnvironment>(MockBehavior.Strict);
            environmentMock
                .Setup(e => e.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime))
                .Returns("python");

            var serviceMap = new Dictionary<Type, object>();
            var scriptHostManager = new TestScriptHostService(ScriptSettingsManager.BuildDefaultConfiguration(), serviceMap);

            var services = new ServiceCollection();
            services.AddSingleton<IEnvironment>(environmentMock.Object);
            services.AddSingleton<IScriptHostManager>(scriptHostManager);
            var serviceProvider = services.BuildServiceProvider();

            var logger = new Mock<ILogger<WebHostWorkerRuntimeResolverAdapter>>();
            var adapter = new WebHostWorkerRuntimeResolverAdapter(serviceProvider, logger.Object);

            var result = adapter.GetWorkerRuntime();

            environmentMock.Verify(e => e.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime), Times.Once);
            Assert.Equal("python", result);
        }

        [Fact]
        public void GetWorkerRuntime_DefaultValue_Returned_WhenResolverAndEnvironmentMissing()
        {
            // Arrange: No script host resolver and no environment variable set
            var environmentMock = new Mock<IEnvironment>(MockBehavior.Strict);
            environmentMock
                .Setup(e => e.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime))
                .Returns((string)null);

            var serviceMap = new Dictionary<Type, object>();
            var scriptHostManager = new TestScriptHostService(ScriptSettingsManager.BuildDefaultConfiguration(), serviceMap);

            var services = new ServiceCollection();
            services.AddSingleton<IEnvironment>(environmentMock.Object);
            services.AddSingleton<IScriptHostManager>(scriptHostManager);
            var serviceProvider = services.BuildServiceProvider();

            var logger = new Mock<ILogger<WebHostWorkerRuntimeResolverAdapter>>();
            var adapter = new WebHostWorkerRuntimeResolverAdapter(serviceProvider, logger.Object);

            var result = adapter.GetWorkerRuntime(defaultValue: "fallback");

            environmentMock.Verify(e => e.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime), Times.Once);
            Assert.Equal("fallback", result);
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

            var logger = new Mock<ILogger<WebHostWorkerRuntimeResolverAdapter>>();
            var adapter = new WebHostWorkerRuntimeResolverAdapter(serviceProvider, logger.Object);

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
    }
}
