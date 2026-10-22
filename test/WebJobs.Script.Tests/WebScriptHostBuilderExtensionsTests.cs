// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Composition;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Composition;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class WebScriptHostBuilderExtensionsTests : IDisposable
    {
        private readonly string _scriptPath = Path.Combine(Path.GetTempPath(), $"ScriptHostBuilder-{Guid.NewGuid():N}");

        public WebScriptHostBuilderExtensionsTests()
        {
            Directory.CreateDirectory(_scriptPath);
            File.WriteAllText(Path.Combine(_scriptPath, "host.json"), """{"version":"2.0"}""");
        }

        [Fact]
        public void Test()
        {
            var builder = new HostBuilder().ConfigureDefaultTestWebScriptHost(null, ConfigureScriptPath, false, configureRootServices: s =>
                {
                    s.AddSingleton<IEnvironment>(p =>
                    {
                        return new TestEnvironment();
                    });

                    var stateProvider = new Mock<IDebugStateProvider>();

                    stateProvider.Setup(d => d.InDebugMode)
                    .Returns(() => true);

                    s.AddSingleton<IDebugStateProvider>(stateProvider.Object);
                });

            using IHost host = builder.Build();

            var hostingEnvironment = host.Services.GetRequiredService<IHostEnvironment>();

            Assert.False(hostingEnvironment.IsDevelopment());
        }

        [Fact]
        public void AddWebScriptHost_ForwardsRootDeferredLogSource_ToScriptHost()
        {
            var rootSource = new DeferredLogSource();

            using IHost host = new HostBuilder()
                .ConfigureDefaultTestWebScriptHost(null, ConfigureScriptPath, false, configureRootServices: s =>
                {
                    s.AddSingleton(rootSource);
                })
                .Build();

            // The ScriptHost's forwarding service must read the WebHost's shared buffer, not a separate instance.
            Assert.Same(rootSource, host.Services.GetService<DeferredLogSource>());
        }

        [Fact]
        public void AddWebScriptHost_UsesFallbackDeferredLogSource_WhenRootHasNone()
        {
            // ConfigureDefaultTestWebScriptHost doesn't register a DeferredLogSource; the ScriptHost falls back to a local buffer.
            using IHost host = new HostBuilder()
                .ConfigureDefaultTestWebScriptHost(configure: ConfigureScriptPath)
                .Build();

            Assert.NotNull(host.Services.GetService<DeferredLogSource>());
        }

        [Fact]
        public void AddWebScriptHost_ThrowsWhenRootServiceProviderIsNull()
        {
            var builder = new HostBuilder();

            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => builder.AddWebScriptHost(
                    rootServiceProvider: null,
                    rootServices: new ServiceCollection(),
                    webHostOptions: new ScriptApplicationHostOptions()));

            Assert.True(string.Equals("rootServiceProvider", exception.ParamName, StringComparison.Ordinal));
        }

        [Fact]
        public void AddWebScriptHost_UsesSelectedComposition()
        {
            var composition = new RecordingWorkerComposition(addCommonRpcServices: true);

            using IHost host = new HostBuilder()
                .ConfigureDefaultTestWebScriptHost(configureWebJobs: null, configure: ConfigureScriptPath, composition: composition)
                .Build();

            Assert.Equal(1, composition.ScriptHostConfigurationCount);
        }

        [Fact]
        public void AddWebScriptHost_CompatibilityOverloadUsesRootSelection()
        {
            var composition = new RecordingWorkerComposition(addCommonRpcServices: false);

            using IHost host = new HostBuilder()
                .ConfigureDefaultTestWebScriptHost(
                    configureWebJobs: null,
                    configure: ConfigureScriptPath,
                    configureRootServices: services => services.AddSingleton(new SelectedWorkerComposition(composition)))
                .Build();

            Assert.Equal(1, composition.ScriptHostConfigurationCount);
        }

        [Fact]
        public void AddWebScriptHost_WorkerConfigurationIsLoadedBeforeLateConfigurationHooks()
        {
            HostJsonConfigurationSnapshot snapshot = HostJsonConfigurationSnapshot.Parse(
                """{"version":"2.0","functionTimeout":"00:03:00","extensions":{"custom":{"setting":"worker"}}}""",
                new(new ScriptApplicationHostOptions()));
            int builds = 0;
            var source = new Mock<IConfigurationSource>();
            source.Setup(value => value.Build(It.IsAny<IConfigurationBuilder>()))
                .Returns((IConfigurationBuilder builder) =>
                {
                    builds++;
                    return snapshot.Build(builder);
                });
            var sourceProvider = new Mock<IHostJsonConfigurationSourceProvider>();
            sourceProvider.Setup(value => value.GetConfigurationSource()).Returns(source.Object);
            var lateHook = new Mock<IConfigureBuilder<IConfigurationBuilder>>();
            lateHook.Setup(value => value.Configure(It.IsAny<IConfigurationBuilder>()))
                .Callback<IConfigurationBuilder>(builder =>
                {
                    Assert.True(builds > 0);
                    Assert.Contains(source.Object, builder.Sources);
                    Assert.Empty(builder.Sources.OfType<HostJsonFileConfigurationSource>());
                });

            using IHost host = new HostBuilder()
                .ConfigureDefaultTestWebScriptHost(configureWebJobs: null, configure: ConfigureScriptPath, configureRootServices: services =>
                {
                    services.AddSingleton(sourceProvider.Object);
                    services.AddSingleton(lateHook.Object);
                })
                .Build();

            IConfiguration configuration = host.Services.GetRequiredService<IConfiguration>();
            Assert.Equal("00:03:00", configuration["AzureFunctionsJobHost:functionTimeout"]);
            Assert.Equal("worker", configuration["AzureFunctionsJobHost:extensions:custom:setting"]);
            lateHook.Verify(value => value.Configure(It.IsAny<IConfigurationBuilder>()), Times.Once);
        }

        [Fact]
        public void AddWebScriptHost_EnabledSourceWithoutAcceptedConfigurationDoesNotFallBackToFile()
        {
            var sourceProvider = new Mock<IHostJsonConfigurationSourceProvider>();
            sourceProvider.Setup(value => value.GetConfigurationSource())
                .Throws(new InvalidOperationException("Configuration has not been accepted."));
            IHostBuilder builder = new HostBuilder()
                .ConfigureDefaultTestWebScriptHost(configureWebJobs: null, configure: ConfigureScriptPath,
                    configureRootServices: services => services.AddSingleton(sourceProvider.Object));

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => builder.Build());

            Assert.Equal("Configuration has not been accepted.", exception.Message);
        }

        [Fact]
        public void AddWebScriptHost_WorkerBundleSettingsAreUsedByExtensionPrebuild()
        {
            HostJsonConfigurationSnapshot snapshot = HostJsonConfigurationSnapshot.Parse(
                """{"version":"2.0","extensionBundle":{"id":"Microsoft.Azure.Functions.ExtensionBundle","version":"invalid"}}""",
                new(new ScriptApplicationHostOptions()));
            var sourceProvider = new Mock<IHostJsonConfigurationSourceProvider>();
            sourceProvider.Setup(value => value.GetConfigurationSource()).Returns(snapshot);
            var lateHook = new Mock<IConfigureBuilder<IConfigurationBuilder>>();
            IHostBuilder builder = new HostBuilder()
                .ConfigureDefaultTestWebScriptHost(configureWebJobs: null, configure: ConfigureScriptPath, configureRootServices: services =>
                {
                    services.AddSingleton(sourceProvider.Object);
                    services.AddSingleton(lateHook.Object);
                });

            ArgumentException exception = Assert.Throws<ArgumentException>(() => builder.Build());

            Assert.StartsWith("The value of version property in extensionBundle section of host.json file is invalid or missing.", exception.Message);
            lateHook.Verify(value => value.Configure(It.IsAny<IConfigurationBuilder>()), Times.Never);
        }

        [Fact]
        public void AddWebScriptHost_EnvironmentOverridesWorkerConfiguration()
        {
            using TestScopedEnvironmentVariable environment = new("AzureFunctionsJobHost__functionTimeout", "00:04:00");
            HostJsonConfigurationSnapshot snapshot = HostJsonConfigurationSnapshot.Parse(
                """{"version":"2.0","functionTimeout":"00:03:00"}""", new(new ScriptApplicationHostOptions()));
            var sourceProvider = new Mock<IHostJsonConfigurationSourceProvider>();
            sourceProvider.Setup(value => value.GetConfigurationSource()).Returns(snapshot);

            using IHost host = new HostBuilder()
                .ConfigureDefaultTestWebScriptHost(configureWebJobs: null, configure: ConfigureScriptPath,
                    configureRootServices: services => services.AddSingleton(sourceProvider.Object))
                .Build();

            Assert.Equal("00:04:00", host.Services.GetRequiredService<IConfiguration>()["AzureFunctionsJobHost:functionTimeout"]);
        }

        public void Dispose()
        {
            Directory.Delete(_scriptPath, recursive: true);
        }

        private void ConfigureScriptPath(ScriptApplicationHostOptions options) => options.ScriptPath = _scriptPath;

        private sealed class RecordingWorkerComposition : IWorkerComposition
        {
            private readonly bool _addCommonRpcServices;

            public RecordingWorkerComposition(bool addCommonRpcServices)
            {
                _addCommonRpcServices = addCommonRpcServices;
            }

            public int ScriptHostConfigurationCount { get; private set; }

            public void ConfigureWebHostServices(IServiceCollection services, IMvcBuilder mvcBuilder)
            {
                ServerWorkerComposition.Instance.ConfigureWebHostServices(services, mvcBuilder);
            }

            public void ConfigureScriptHostServices(IServiceCollection services, IServiceProvider rootServiceProvider)
            {
                ScriptHostConfigurationCount++;
                ServerWorkerComposition.Instance.ConfigureScriptHostServices(services, rootServiceProvider);
                if (_addCommonRpcServices)
                {
                    services.AddCommonRpcServices();
                }
            }
        }
    }
}