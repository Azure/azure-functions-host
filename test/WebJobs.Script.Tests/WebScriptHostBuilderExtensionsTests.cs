// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Composition;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Composition;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class WebScriptHostBuilderExtensionsTests
    {
        [Fact]
        public void Test()
        {
            var builder = new HostBuilder().ConfigureDefaultTestWebScriptHost(null, null, false, configureRootServices: s =>
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

            var host = builder.Build();

            var hostingEnvironment = host.Services.GetRequiredService<IHostEnvironment>();

            Assert.False(hostingEnvironment.IsDevelopment());
        }

        [Fact]
        public void AddWebScriptHost_ForwardsRootDeferredLogSource_ToScriptHost()
        {
            var rootSource = new DeferredLogSource();

            var host = new HostBuilder()
                .ConfigureDefaultTestWebScriptHost(null, null, false, configureRootServices: s =>
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
            var host = new HostBuilder()
                .ConfigureDefaultTestWebScriptHost()
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
                .ConfigureDefaultTestWebScriptHost(configureWebJobs: null, composition: composition)
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
                    configureRootServices: services => services.AddSingleton(new SelectedWorkerComposition(composition)))
                .Build();

            Assert.Equal(1, composition.ScriptHostConfigurationCount);
        }

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