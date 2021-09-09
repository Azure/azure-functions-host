﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Logging.ApplicationInsights;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Middleware;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Diagnostics.JitTrace;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Xunit;
using Xunit.Abstractions;
using IApplicationLifetime = Microsoft.AspNetCore.Hosting.IApplicationLifetime;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class SpecializationE2ETests
    {
        private static SemaphoreSlim _pauseBeforeHostBuild;
        private static SemaphoreSlim _pauseAfterStandbyHostBuild;
        private static SemaphoreSlim _buildCount;

        private static readonly string _standbyPath = Path.Combine(Path.GetTempPath(), "functions", "standby", "wwwroot");
        private const string _specializedScriptRoot = @"TestScripts\CSharp";

        private readonly TestEnvironment _environment;
        private readonly TestLoggerProvider _loggerProvider;

        private readonly ITestOutputHelper _testOutputHelper;

        public SpecializationE2ETests(ITestOutputHelper testOutputHelper)
        {
            StandbyManager.ResetChangeToken();

            var settings = new Dictionary<string, string>()
            {
                { EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1" },
                { EnvironmentSettingNames.AzureWebsiteContainerReady, null },
             };

            _environment = new TestEnvironment(settings);
            _loggerProvider = new TestLoggerProvider();

            _pauseBeforeHostBuild = new SemaphoreSlim(1, 1);
            _pauseAfterStandbyHostBuild = new SemaphoreSlim(1, 1);
            _buildCount = new SemaphoreSlim(2, 2);

            _testOutputHelper = testOutputHelper;
        }

        [Fact (Skip = "https://github.com/Azure/azure-functions-host/issues/7657")]
        public async Task ApplicationInsights_InvocationsContainDifferentOperationIds()
        {
            // Verify that when a request specializes the host we don't capture the context
            // of that request. Application Insights uses this context to correlate telemetry
            // so it had a confusing effect. Previously all TimerTrigger traces would have the
            // operation id of this request and all host logs would as well.
            var channel = new TestTelemetryChannel();

            var builder = CreateStandbyHostBuilder("OneSecondTimer", "FunctionExecutionContext")
                .ConfigureScriptHostServices(s =>
                {
                    s.AddSingleton<ITelemetryChannel>(_ => channel);

                    s.Configure<FunctionResultAggregatorOptions>(o =>
                    {
                        o.IsEnabled = false;
                    });

                    s.PostConfigure<ApplicationInsightsLoggerOptions>(o =>
                    {
                        o.SamplingSettings = null;
                    });
                })
                .ConfigureScriptHostAppConfiguration(c =>
                {
                    c.AddInMemoryCollection(new Dictionary<string, string>
                    {
                        [EnvironmentSettingNames.AppInsightsInstrumentationKey] = "some_key"
                    });
                });

            // TODO: https://github.com/Azure/azure-functions-host/issues/4876
            using (var testServer = new TestServer(builder))
            {
                var client = testServer.CreateClient();

                HttpResponseMessage response = await client.GetAsync("api/warmup");
                Assert.True(response.IsSuccessStatusCode, _loggerProvider.GetLog());

                // Now that standby mode is warmed up, set the specialization properties...
                _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteContainerReady, "1");
                _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");

                // ...and issue a request which will force specialization.
                response = await client.GetAsync("api/functionexecutioncontext");
                Assert.True(response.IsSuccessStatusCode, _loggerProvider.GetLog());

                // Wait until we have a few logs from the timer trigger.
                IEnumerable<TraceTelemetry> timerLogs = null;
                await TestHelpers.Await(() =>
                {
                    timerLogs = channel.Telemetries
                        .OfType<TraceTelemetry>()
                        .Where(p => p.Message == "OneSecondTimer fired!");

                    return timerLogs.Count() >= 3;
                });

                var startupRequest = channel.Telemetries
                    .OfType<RequestTelemetry>()
                    .Where(p => p.Name == "FunctionExecutionContext")
                    .Single();

                // Make sure that auto-Http tracking worked with this request.
                Assert.Equal("200", startupRequest.ResponseCode);

                // The host logs should not be associated with this request.
                var logsWithRequestId = channel.Telemetries
                    .OfType<TraceTelemetry>()
                    .Select(p => p.Context.Operation.Id)
                    .Where(p => p == startupRequest.Context.Operation.Id);

                // Just expect the "Executing" and "Executed" logs from the actual request.
                Assert.Equal(2, logsWithRequestId.Count());

                // And each of the timer invocations should have a different operation id, and none
                // should match the request id.
                var distinctOpIds = timerLogs.Select(p => p.Context.Operation.Id).Distinct();
                Assert.Equal(timerLogs.Count(), distinctOpIds.Count());
                Assert.Empty(timerLogs.Where(p => p.Context.Operation.Id == startupRequest.Context.Operation.Id));
            }
        }

        [Fact]
        public async Task Specialization_ThreadUtilization()
        {
            var builder = CreateStandbyHostBuilder("FunctionExecutionContext");

            // TODO: https://github.com/Azure/azure-functions-host/issues/4876
            using (var testServer = new TestServer(builder))
            {
                var client = testServer.CreateClient();

                var response = await client.GetAsync("api/warmup");
                response.EnsureSuccessStatusCode();

                List<Task<HttpResponseMessage>> requestTasks = new List<Task<HttpResponseMessage>>();

                _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteContainerReady, "1");
                _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");

                await _pauseBeforeHostBuild.WaitAsync();

                ThreadPool.GetAvailableThreads(out int originalWorkerThreads, out int originalcompletionThreads);

                for (int i = 0; i < 100; i++)
                {
                    requestTasks.Add(client.GetAsync("api/functionexecutioncontext"));
                }

                Thread.Sleep(5000);
                ThreadPool.GetAvailableThreads(out int workerThreads, out int completionThreads);

                _pauseBeforeHostBuild.Release();

                // Before the fix, when we issued the 100 requests, they would all enter the ThreadPool queue and
                // a new thread would be taken from the thread pool every 500ms, resulting in thread starvation.
                // After the fix, we should only be losing one (but other operations may also be using a thread, so 
                // we'll leave a little wiggle-room).
                int precision = 3;
                Assert.True(workerThreads >= originalWorkerThreads - precision, $"Available ThreadPool threads should not have decreased by more than {precision}. Actual: {workerThreads}. Original: {originalWorkerThreads}.");

                await Task.WhenAll(requestTasks);

                void ValidateStatusCode(HttpStatusCode statusCode) => Assert.Equal(HttpStatusCode.OK, statusCode);
                var validateStatusCodes = Enumerable.Repeat<Action<HttpStatusCode>>(ValidateStatusCode, 100).ToArray();
                var actualStatusCodes = requestTasks.Select(t => t.Result.StatusCode);

                try
                {
                    Assert.Collection(actualStatusCodes, validateStatusCodes);
                }
                catch
                {
                    foreach (var message in _loggerProvider.GetAllLogMessages())
                    {
                        _testOutputHelper.WriteLine(message.FormattedMessage);
                    }

                    throw;
                }
            }
        }

        [Fact]
        public async Task Specialization_ResetsSharedLoadContext()
        {
            var builder = CreateStandbyHostBuilder("FunctionExecutionContext");

            using (var testServer = new TestServer(builder))
            {
                var client = testServer.CreateClient();

                var response = await client.GetAsync("api/warmup");
                response.EnsureSuccessStatusCode();

                var placeholderContext = FunctionAssemblyLoadContext.Shared;

                _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteContainerReady, "1");
                _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");

                //await _pauseBeforeHostBuild.WaitAsync(10000);

                response = await client.GetAsync("api/functionexecutioncontext");
                response.EnsureSuccessStatusCode();

                var specializedContext = FunctionAssemblyLoadContext.Shared;

                Assert.NotSame(placeholderContext, specializedContext);
            }
        }

        [Fact]
        public async Task StartAsync_SetsCorrectActiveHost_RefreshesLanguageWorkerOptions()
        {
            var builder = CreateStandbyHostBuilder();

            await _pauseAfterStandbyHostBuild.WaitAsync();

            // We want it to start first, but finish last, so unstick it in a couple seconds.
            Task ignore = Task.Delay(3000).ContinueWith(_ => _pauseAfterStandbyHostBuild.Release());

            IWebHost host = builder.Build();
            var scriptHostService = host.Services.GetService<WebJobsScriptHostService>();
            var channelFactory = host.Services.GetService<IRpcWorkerChannelFactory>();
            var workerOptionsPlaceholderMode = host.Services.GetService<IOptions<LanguageWorkerOptions>>();
            Assert.Equal(4, workerOptionsPlaceholderMode.Value.WorkerConfigs.Count);
            var rpcChannelInPlaceholderMode = (GrpcWorkerChannel)channelFactory.Create("/", "powershell", null, 0, workerOptionsPlaceholderMode.Value.WorkerConfigs);
            Assert.Equal("7", rpcChannelInPlaceholderMode.Config.Description.DefaultRuntimeVersion);


            // TestServer will block in the constructor so pull out the StandbyManager and use it
            // directly for this test.
            var standbyManager = host.Services.GetService<IStandbyManager>();

            var standbyStart = Task.Run(async () => await scriptHostService.StartAsync(CancellationToken.None));

            // Wait until we've completed the build once. The standby host is built and now waiting for
            // _pauseAfterHostBuild to release it.
            await TestHelpers.Await(() => _buildCount.CurrentCount == 1);

            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteContainerReady, "1");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");
            _environment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName, "powershell");
            _environment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeVersionSettingName, "7");

            var specializeTask = Task.Run(async () => await standbyManager.SpecializeHostAsync());

            await Task.WhenAll(standbyStart, specializeTask);

            var options = scriptHostService.Services.GetService<IOptions<ScriptJobHostOptions>>();
            Assert.Equal(_specializedScriptRoot, options.Value.RootScriptPath);

            var workerOptionsAtJobhostLevel = scriptHostService.Services.GetService<IOptions<LanguageWorkerOptions>>();
            Assert.Equal(1, workerOptionsAtJobhostLevel.Value.WorkerConfigs.Count);
            var rpcChannelAfterSpecialization = (GrpcWorkerChannel)channelFactory.Create("/", "powershell", null, 0, workerOptionsAtJobhostLevel.Value.WorkerConfigs);
            Assert.Equal("7", rpcChannelAfterSpecialization.Config.Description.DefaultRuntimeVersion);
        }

        /// <summary>
        /// Loads an extension that requires Host secrets and needs connection to storage.
        /// This happens when the ActiveHost changes as a new JobHost is initialized
        /// </summary>
        [Fact]
        public async Task Specialization_LoadWebHookProviderAndRetrieveSecrets()
        {
            var storageValue = TestHelpers.GetTestConfiguration().GetWebJobsConnectionString("AzureWebJobsStorage");

            // We can't assume the placeholder has any environment variables specified by the customer.
            // Add environment variables expected throughout the specialization (similar to how DWAS updates the environment)
            using (new TestScopedEnvironmentVariable("AzureWebJobsStorage", ""))
            {
                var builder = CreateStandbyHostBuilder("FunctionExecutionContext")
                .ConfigureScriptHostWebJobsBuilder(s =>
                {
                    if (!_environment.IsPlaceholderModeEnabled())
                    {
                        // Add an extension that calls GetUrl(), which can cause secrets to be loaded
                        // before the host is initialized.
                        s.Services.AddSingleton<IExtensionConfigProvider, TestWebHookExtension>();
                    }
                });

                // This is required to force secrets to load.
                _environment.SetEnvironmentVariable("WEBSITE_HOSTNAME", "test");

                using (var testServer = new TestServer(builder))
                {
                    var client = testServer.CreateClient();
                    var response = await client.GetAsync("api/warmup");
                    response.EnsureSuccessStatusCode();

                    _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteContainerReady, "1");
                    _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");

                    // This value is available now
                    using (new TestScopedEnvironmentVariable("AzureWebJobsStorage", storageValue))
                    {
                        // Now that we're specialized, set up the expected env var, which will be loaded internally
                        // when the config is refreshed during specialization.
                        // This request will force specialization.
                        response = await client.GetAsync("api/functionexecutioncontext");
                        response.EnsureSuccessStatusCode();
                    }
                }
            }
        }

        /// <summary>
        /// This scenario tests that storage can still be used 
        /// </summary>
        [Fact]
        public async Task Specialization_CustomStartupRemovesAzureWebJobsStorage()
        {
            var storageValue = TestHelpers.GetTestConfiguration().GetWebJobsConnectionString("AzureWebJobsStorage");

            // We can't assume the placeholder has any environment variables specified by the customer.
            // Add environment variables expected throughout the specialization (similar to how DWAS updates the environment)
            using (new TestScopedEnvironmentVariable("AzureWebJobsStorage", ""))
            {
                var builder = CreateStandbyHostBuilder("FunctionExecutionContext")
                .ConfigureScriptHostWebJobsBuilder(s =>
                {
                    if (!_environment.IsPlaceholderModeEnabled())
                    {
                        // Add an extension that calls GetUrl(), which can cause secrets to be loaded
                        // before the host is initialized.
                        s.Services.AddSingleton<IExtensionConfigProvider, TestWebHookExtension>();
                    }
                })
                .ConfigureScriptHostServices(s =>
                {
                    // Override the IConfiguration of the ScriptHost to empty configuration
                    s.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
                });

                // This is required to force secrets to load.
                _environment.SetEnvironmentVariable("WEBSITE_HOSTNAME", "test");

                using (var testServer = new TestServer(builder))
                {
                    var client = testServer.CreateClient();
                    var response = await client.GetAsync("api/warmup");
                    response.EnsureSuccessStatusCode();

                    _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteContainerReady, "1");
                    _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");

                    // This value is available now
                    using (new TestScopedEnvironmentVariable("AzureWebJobsStorage", storageValue))
                    {
                        // Now that we're specialized, set up the expected env var, which will be loaded internally
                        // when the config is refreshed during specialization.
                        // This request will force specialization.
                        response = await client.GetAsync("api/functionexecutioncontext");
                        response.EnsureSuccessStatusCode();
                    }
                }
            }
        }

        [Fact]
        public async Task Specialization_CustomStartupAddsWebJobsStorage()
        {
            var storageValue = TestHelpers.GetTestConfiguration().GetWebJobsConnectionString("AzureWebJobsStorage");

            // No AzureWebJobsStorage set in environment variables (App Settings from portal)
            using (new TestScopedEnvironmentVariable("AzureWebJobsStorage", ""))
            {
                var builder = CreateStandbyHostBuilder("FunctionExecutionContext")
                .ConfigureScriptHostWebJobsBuilder(s =>
                {
                    if (!_environment.IsPlaceholderModeEnabled())
                    {
                        // Add an extension that calls GetUrl(), which can cause secrets to be loaded
                        // before the host is initialized.
                        s.Services.AddSingleton<IExtensionConfigProvider, TestWebHookExtension>();
                    }
                })
                .ConfigureScriptHostAppConfiguration(c =>
                {
                    if (!_environment.IsPlaceholderModeEnabled())
                    {
                        var inMemoryCollection = new Dictionary<string, string>()
                        {
                            { "AzureWebJobsStorage", storageValue }
                        };
                        c.AddInMemoryCollection(inMemoryCollection);
                    }
                });

                // This is required to force secrets to load.
                _environment.SetEnvironmentVariable("WEBSITE_HOSTNAME", "test");

                using (var testServer = new TestServer(builder))
                {
                    var client = testServer.CreateClient();
                    var response = await client.GetAsync("api/warmup");
                    response.EnsureSuccessStatusCode();

                    _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteContainerReady, "1");
                    _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");

                    // Now that we're specialized, set up the expected env var, which will be loaded internally
                    // when the config is refreshed during specialization.
                    // This request will force specialization.
                    response = await client.GetAsync("api/functionexecutioncontext");
                    response.EnsureSuccessStatusCode();
                }
            }
        }

        private IWebHostBuilder CreateStandbyHostBuilder(params string[] functions)
        {
            string scriptRootConfigPath = ConfigurationPath.Combine(ConfigurationSectionNames.WebHost, nameof(ScriptApplicationHostOptions.ScriptPath));

            var builder = Program.CreateWebHostBuilder()
                .ConfigureLogging(b =>
                {
                    b.AddProvider(_loggerProvider);
                })
                .ConfigureAppConfiguration(c =>
                {
                    c.AddInMemoryCollection(new Dictionary<string, string>
                    {
                        { scriptRootConfigPath, _specializedScriptRoot }
                    });
                })
                .ConfigureServices((bc, s) =>
                {
                    s.AddSingleton<IEnvironment>(_environment);

                    // Ensure that we don't have a race between the timer and the 
                    // request for triggering specialization.
                    s.AddSingleton<IStandbyManager, InfiniteTimerStandbyManager>();

                    s.AddSingleton<IScriptHostBuilder, PausingScriptHostBuilder>();
                })
                .ConfigureScriptHostServices(s =>
                {
                    s.PostConfigure<ScriptJobHostOptions>(o =>
                    {
                        // Only load the function we care about, but not during standby
                        if (o.RootScriptPath != _standbyPath)
                        {
                            o.Functions = functions;
                        }
                    });
                });

            return builder;
        }

        private class InfiniteTimerStandbyManager : StandbyManager
        {
            public InfiniteTimerStandbyManager(IScriptHostManager scriptHostManager, IWebHostRpcWorkerChannelManager rpcWorkerChannelManager,
                IConfiguration configuration, IScriptWebHostEnvironment webHostEnvironment, IEnvironment environment,
                IOptionsMonitor<ScriptApplicationHostOptions> options, ILogger<StandbyManager> logger, HostNameProvider hostNameProvider, IApplicationLifetime applicationLifetime)
                : base(scriptHostManager, rpcWorkerChannelManager, configuration, webHostEnvironment, environment, options,
                      logger, hostNameProvider, applicationLifetime, TimeSpan.FromMilliseconds(-1), new TestMetricsLogger())
            {
            }
        }

        private class PausingScriptHostBuilder : IScriptHostBuilder
        {
            private readonly DefaultScriptHostBuilder _inner;
            private readonly IOptionsMonitor<ScriptApplicationHostOptions> _options;

            public PausingScriptHostBuilder(IOptionsMonitor<ScriptApplicationHostOptions> options, IServiceProvider root, IServiceScopeFactory scope)
            {
                _inner = new DefaultScriptHostBuilder(options, root, scope);
                _options = options;
            }

            public IHost BuildHost(bool skipHostStartup, bool skipHostConfigurationParsing)
            {
                bool isStandby = _options.CurrentValue.ScriptPath == _standbyPath;

                _pauseBeforeHostBuild.WaitAsync().GetAwaiter().GetResult();
                _pauseBeforeHostBuild.Release();

                IHost host = _inner.BuildHost(skipHostStartup, skipHostConfigurationParsing);

                _buildCount.Wait();

                if (isStandby)
                {
                    _pauseAfterStandbyHostBuild.WaitAsync().GetAwaiter().GetResult();
                    _pauseAfterStandbyHostBuild.Release();
                }

                _buildCount.Release();

                return host;
            }
        }

        private class TestWebHookExtension : IExtensionConfigProvider, IAsyncConverter<HttpRequestMessage, HttpResponseMessage>
        {
            private readonly IWebHookProvider _webHookProvider;
            public TestWebHookExtension(IWebHookProvider webHookProvider)
            {
                _webHookProvider = webHookProvider;
            }
            public void Initialize(ExtensionConfigContext context)
            {
                _webHookProvider.GetUrl(this);
            }
            public Task<HttpResponseMessage> ConvertAsync(HttpRequestMessage input, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }
        }
    }
}