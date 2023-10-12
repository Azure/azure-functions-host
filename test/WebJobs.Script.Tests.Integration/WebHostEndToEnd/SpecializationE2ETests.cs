// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Queue;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Logging.ApplicationInsights;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Xunit;
using Xunit.Abstractions;
using IApplicationLifetime = Microsoft.AspNetCore.Hosting.IApplicationLifetime;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class SpecializationE2ETests
    {
        private static SemaphoreSlim _pauseBeforeHostBuild;
        private static SemaphoreSlim _pauseAfterStandbyHostBuild;
        private static SemaphoreSlim _buildCount;

        private static readonly string _standbyPath = Path.Combine(Path.GetTempPath(), "functions", "standby", "wwwroot");
        private static readonly string _scriptRootConfigPath = ConfigurationPath.Combine(ConfigurationSectionNames.WebHost, nameof(ScriptApplicationHostOptions.ScriptPath));

        private static readonly string _dotnetIsolated60Path = Path.GetFullPath(@"..\..\..\..\DotNetIsolated60\bin\Debug\net6.0");
        private static readonly string _dotnetIsolatedUnsuppportedPath = Path.GetFullPath(@"..\..\..\..\DotNetIsolatedUnsupportedWorker\bin\Debug\net6.0");
        private static readonly string _dotnetIsolatedEmptyScriptRoot = Path.GetFullPath(@"..\..\..\..\EmptyScriptRoot");

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

        [Fact]
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
        public async Task ForNonReadOnlyFileSystem_RestartWorkerForSpecializationAndHotReload()
        {
            _environment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName, "node");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, ScriptConstants.FeatureFlagEnableWorkerIndexing);

            var builder = CreateStandbyHostBuilder("HttpTriggerNoAuth");

            builder.ConfigureAppConfiguration(config =>
            {
                string scriptRootConfigPath = ConfigurationPath.Combine(ConfigurationSectionNames.WebHost, nameof(ScriptApplicationHostOptions.ScriptPath));
                config.AddInMemoryCollection(new Dictionary<string, string>
                {
                    { _scriptRootConfigPath, Path.GetFullPath(@"TestScripts\NodeWithBundles") }
                });
            });

            using var testServer = new TestServer(builder);

            var client = testServer.CreateClient();

            var response = await client.GetAsync("api/warmup");
            response.EnsureSuccessStatusCode();

            var webChannelManager = testServer.Services.GetService<IWebHostRpcWorkerChannelManager>();
            var channel = await webChannelManager.GetChannels("node").Single().Value.Task;
            var processId = channel.WorkerProcess.Process.Id;

            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteContainerReady, "1");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");

            response = await client.GetAsync("api/HttpTriggerNoAuth");
            response.EnsureSuccessStatusCode();
            var responseContent = await response.Content.ReadAsStringAsync();

            string content = "Node.js HttpTrigger function invoked.";
            responseContent.Contains(content);

            channel = await webChannelManager.GetChannels("node").Single().Value.Task;
            var newProcessId = channel.WorkerProcess.Process.Id;
            Assert.NotEqual(processId, newProcessId);
            Assert.Contains(content, responseContent);

            var indexJS = Path.GetFullPath(@"TestScripts\NodeWithBundles\HttpTriggerNoAuth\index.js");

            string fileContent = File.ReadAllText(indexJS);
            string newContent = "Updated Node.js HttpTrigger function invoked.";
            string updatedContent = fileContent.Replace(content, newContent);
            File.WriteAllText(indexJS, updatedContent);

            var manager = testServer.Host.Services.GetService<IScriptHostManager>();
            var hostService = manager as WebJobsScriptHostService;

            await TestHelpers.Await(() =>
            {
                return hostService.State == ScriptHostState.Default;
            }, 5000);

            await TestHelpers.Await(() =>
            {
                return hostService.State == ScriptHostState.Running;
            }, 5000);

            response = await client.GetAsync("api/HttpTriggerNoAuth");
            response.EnsureSuccessStatusCode();
            responseContent = await response.Content.ReadAsStringAsync();
            responseContent.Contains(newContent);

            channel = await webChannelManager.GetChannels("node").Single().Value.Task;
            var hotReloadProcessId = channel.WorkerProcess.Process.Id;
            Assert.NotEqual(hotReloadProcessId, newProcessId);
            Assert.Contains(newContent, responseContent);
        }

        [Fact]
        public async Task Specialization_RestartsWorkerForNonReadOnlyFileSystem()
        {
            _environment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName, "node");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, ScriptConstants.FeatureFlagEnableWorkerIndexing);

            var builder = CreateStandbyHostBuilder("HttpTriggerNoAuth");

            builder.ConfigureAppConfiguration(config =>
            {
                string scriptRootConfigPath = ConfigurationPath.Combine(ConfigurationSectionNames.WebHost, nameof(ScriptApplicationHostOptions.ScriptPath));
                config.AddInMemoryCollection(new Dictionary<string, string>
                {
                    { _scriptRootConfigPath, Path.GetFullPath(@"TestScripts\NodeWithBundles") }
                });
            });

            using var testServer = new TestServer(builder);

            var client = testServer.CreateClient();

            var response = await client.GetAsync("api/warmup");
            response.EnsureSuccessStatusCode();

            var placeholderContext = FunctionAssemblyLoadContext.Shared;

            var webChannelManager = testServer.Services.GetService<IWebHostRpcWorkerChannelManager>();
            var channel = await webChannelManager.GetChannels("node").Single().Value.Task;
            var processId = channel.WorkerProcess.Process.Id;

            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteContainerReady, "1");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");

            //await _pauseBeforeHostBuild.WaitAsync(10000);
            response = await client.GetAsync("api/HttpTriggerNoAuth");
            response.EnsureSuccessStatusCode();

            channel = await webChannelManager.GetChannels("node").Single().Value.Task;
            var newProcessId = channel.WorkerProcess.Process.Id;
            Assert.NotEqual(processId, newProcessId);
        }


        [Fact]
        public async Task Specialization_UsePlaceholderWorkerforReadOnlyFileSystem()
        {
            _environment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName, "node");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, ScriptConstants.FeatureFlagEnableWorkerIndexing);

            var builder = CreateStandbyHostBuilder("HttpTriggerNoAuth");
            string isFileSystemReadOnly = ConfigurationPath.Combine(ConfigurationSectionNames.WebHost, nameof(ScriptApplicationHostOptions.IsFileSystemReadOnly));

            builder.ConfigureAppConfiguration(config =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string>
                    {
                        { _scriptRootConfigPath, Path.GetFullPath(@"TestScripts\NodeWithBundles") }
                    });
                });


            using var testServer = new TestServer(builder);

            var client = testServer.CreateClient();

            var response = await client.GetAsync("api/warmup");
            response.EnsureSuccessStatusCode();

            var webChannelManager = testServer.Services.GetService<IWebHostRpcWorkerChannelManager>();
            var channel = await webChannelManager.GetChannels("node").Single().Value.Task;
            var processId = channel.WorkerProcess.Process.Id;

            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteRunFromPackage, "1");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteContainerReady, "1");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");

            response = await client.GetAsync("api/HttpTriggerNoAuth");
            response.EnsureSuccessStatusCode();

            channel = await webChannelManager.GetChannels("node").Single().Value.Task;
            var newProcessId = channel.WorkerProcess.Process.Id;
            Assert.Equal(processId, newProcessId);
        }

        [Fact]
        public async Task Specialization_RestartWorkerWithWorkerArguments()
        {
            _environment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName, "node");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, ScriptConstants.FeatureFlagEnableWorkerIndexing);

            var builder = CreateStandbyHostBuilder("HttpTriggerNoAuth");
            string isFileSystemReadOnly = ConfigurationPath.Combine(ConfigurationSectionNames.WebHost, nameof(ScriptApplicationHostOptions.IsFileSystemReadOnly));

            builder.ConfigureAppConfiguration(config =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string>
                {
                    { _scriptRootConfigPath, Path.GetFullPath(@"TestScripts\NodeWithBundles") }
                });
            });

            using var testServer = new TestServer(builder);

            var client = testServer.CreateClient();

            var response = await client.GetAsync("api/warmup");
            response.EnsureSuccessStatusCode();

            var webChannelManager = testServer.Services.GetService<IWebHostRpcWorkerChannelManager>();
            var channel = await webChannelManager.GetChannels("node").Single().Value.Task;
            var processId = channel.WorkerProcess.Process.Id;
            Assert.DoesNotContain("--max-old-space-size=1272", channel.WorkerProcess.Process.StartInfo.Arguments);

            // Use an actual env var here as it will be refreshed in config after specialization
            using var envVars = new TestScopedEnvironmentVariable("languageWorkers:node:arguments", "--max-old-space-size=1272");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteRunFromPackage, "1");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteRunFromPackage, "1");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteContainerReady, "1");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");

            response = await client.GetAsync("api/HttpTriggerNoAuth");
            response.EnsureSuccessStatusCode();

            channel = await webChannelManager.GetChannels("node").Single().Value.Task;
            var newProcessId = channel.WorkerProcess.Process.Id;
            Assert.Contains("--max-old-space-size=1272", channel.WorkerProcess.Process.StartInfo.Arguments);
            Assert.NotEqual(processId, newProcessId);
        }

        [Fact]
        public async Task Specialization_GCMode()
        {
            var builder = CreateStandbyHostBuilder("FunctionExecutionContext");

            using (var testServer = new TestServer(builder))
            {
                var client = testServer.CreateClient();

                // GC's LatencyMode should be Interactive as default, switch to NoGCRegion in placeholder mode and back to Interactive when specialization is complete.
                Assert.True(GCSettings.LatencyMode != GCLatencyMode.NoGCRegion, "GCLatencyMode should *not* be NoGCRegion at the beginning");

                var response = await client.GetAsync("api/warmup");
                response.EnsureSuccessStatusCode();

                _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteContainerReady, "1");
                _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");

                response = await client.GetAsync("api/functionexecutioncontext");
                response.EnsureSuccessStatusCode();

                Assert.True(GCSettings.LatencyMode != GCLatencyMode.NoGCRegion, "GCLatencyMode should *not* be NoGCRegion at the end of specialization");
            }
        }

        [Fact]
        public async Task Specialization_ResetsSecretManagerRepository()
        {
            var builder = CreateStandbyHostBuilder("FunctionExecutionContext")
                .ConfigureLogging(logging =>
                {
                    logging.AddFilter<TestLoggerProvider>(null, LogLevel.Debug);
                });

            using (var testServer = new TestServer(builder))
            {
                var client = testServer.CreateClient();

                var response = await client.GetAsync("api/warmup");
                response.EnsureSuccessStatusCode();

                var provider = testServer.Host.Services.GetService<ISecretManagerProvider>();
                _ = provider.SecretsEnabled;
                _ = provider.SecretsEnabled;
                _ = provider.SecretsEnabled;

                // Should only be evaluated once due to the Lazy
                var messages = _loggerProvider.GetAllLogMessages().Select(p => p.EventId.Name);
                Assert.Single(messages, "GetSecretsEnabled");

                _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteContainerReady, "1");
                _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");

                // Force specialization
                response = await client.GetAsync("api/functionexecutioncontext");
                response.EnsureSuccessStatusCode();

                _ = provider.SecretsEnabled;
                _ = provider.SecretsEnabled;
                _ = provider.SecretsEnabled;

                messages = _loggerProvider.GetAllLogMessages().Select(p => p.EventId.Name);

                // Should be re-evaluated one more time after reset
                Assert.Equal(2, messages.Where(p => p == "GetSecretsEnabled").Count());

                Assert.Single(messages, "ResetSecretManager");
            }
        }

        [Fact]
        public async Task StartAsync_SetsCorrectActiveHost_RefreshesLanguageWorkerOptions()
        {
            var builder = CreateStandbyHostBuilder();

            await _pauseAfterStandbyHostBuild.WaitAsync();

            // We want it to start first, but finish last, so unstick it in a couple seconds.
            Task ignore = Task.Delay(3000).ContinueWith(_ => _pauseAfterStandbyHostBuild.Release());

            var expectedPowerShellVersion = "7.2";
            IWebHost host = builder.Build();
            var scriptHostService = host.Services.GetService<WebJobsScriptHostService>();
            var channelFactory = host.Services.GetService<IRpcWorkerChannelFactory>();
            var workerOptionsPlaceholderMode = host.Services.GetService<IOptions<LanguageWorkerOptions>>();
            Assert.Equal(5, workerOptionsPlaceholderMode.Value.WorkerConfigs.Count);
            var rpcChannelInPlaceholderMode = (GrpcWorkerChannel)channelFactory.Create("/", "powershell", null, 0, workerOptionsPlaceholderMode.Value.WorkerConfigs);
            Assert.Equal(expectedPowerShellVersion, rpcChannelInPlaceholderMode.WorkerConfig.Description.DefaultRuntimeVersion);


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
            _environment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeVersionSettingName, expectedPowerShellVersion);

            var specializeTask = Task.Run(async () => await standbyManager.SpecializeHostAsync());

            await Task.WhenAll(standbyStart, specializeTask);

            var options = scriptHostService.Services.GetService<IOptions<ScriptJobHostOptions>>();
            Assert.Equal(_specializedScriptRoot, options.Value.RootScriptPath);

            var workerOptionsAtJobhostLevel = scriptHostService.Services.GetService<IOptions<LanguageWorkerOptions>>();
            Assert.Equal(1, workerOptionsAtJobhostLevel.Value.WorkerConfigs.Count);
            var rpcChannelAfterSpecialization = (GrpcWorkerChannel)channelFactory.Create("/", "powershell", null, 0, workerOptionsAtJobhostLevel.Value.WorkerConfigs);
            Assert.Equal(expectedPowerShellVersion, rpcChannelAfterSpecialization.WorkerConfig.Description.DefaultRuntimeVersion);
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

        /// <summary>
        /// This scenario tests that the configured JobHostInternalStorageOptions will have the right
        /// customer-provided configuration of the ActiveHost after specialization.
        /// Since JobHostInternalStorageOptions is registered at the WebHost, it must react to changes
        /// to the ActiveHost.
        /// </summary>
        [Fact]
        public async Task Specialization_JobHostInternalStorageOptionsUpdatesWithActiveHost()
        {
            var storageValue = TestHelpers.GetTestConfiguration().GetWebJobsConnectionString("AzureWebJobsStorage");

            var blobServiceClient = new BlobServiceClient(storageValue);
            var containerClient = blobServiceClient.GetBlobContainerClient("test-sas-container");
            await containerClient.CreateIfNotExistsAsync(); // this will throw if storageValue is bad;
            var fakeSasUri = containerClient.GenerateSasUri(BlobContainerSasPermissions.Read | BlobContainerSasPermissions.Write | BlobContainerSasPermissions.List, DateTime.UtcNow.AddDays(10));

            // We can't assume the placeholder has any environment variables specified by the customer.
            // Add environment variables expected throughout the specialization (similar to how DWAS updates the environment)
            using (new TestScopedEnvironmentVariable("AzureFunctionsJobHost__InternalSasBlobContainer", ""))
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

                    // Should not be able to get the Hosting BlobContainerClient before specialization since
                    // customer provided storage-related configuration is not in the Environment
                    var blobStorageProvider = testServer.Services.GetService<IAzureBlobStorageProvider>();
                    Assert.False(blobStorageProvider.TryCreateHostingBlobContainerClient(out _));

                    _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteContainerReady, "1");
                    _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");

                    // This value is available now
                    using (new TestScopedEnvironmentVariable("AzureFunctionsJobHost__InternalSasBlobContainer", fakeSasUri.ToString()))
                    using (new TestScopedEnvironmentVariable("AzureWebJobsStorage", storageValue))
                    {
                        // Now that we're specialized, set up the expected env var, which will be loaded internally
                        // when the config is refreshed during specialization.
                        // This request will force specialization.
                        response = await client.GetAsync("api/functionexecutioncontext");
                        response.EnsureSuccessStatusCode();

                        // The HostingBlobContainerClient should be the sas container specified.
                        blobStorageProvider = testServer.Services.GetService<IAzureBlobStorageProvider>();
                        Assert.True(blobStorageProvider.TryCreateHostingBlobContainerClient(out var blobContainerClient));
                        Assert.Equal("test-sas-container", blobContainerClient.Name);
                    }
                }

                await containerClient.DeleteAsync();
            }
        }

        [Fact]
        public async Task DotNetIsolated_PlaceholderHit()
        {
            var builder = InitializeDotNetIsolatedPlaceholderBuilder(_dotnetIsolated60Path, "HttpRequestDataFunction");

            using var testServer = new TestServer(builder);

            var client = testServer.CreateClient();
            var response = await client.GetAsync("api/warmup");
            response.EnsureSuccessStatusCode();

            // Validate that the channel is set up with native worker
            var webChannelManager = testServer.Services.GetService<IWebHostRpcWorkerChannelManager>();

            var placeholderChannel = await webChannelManager.GetChannels("dotnet-isolated").Single().Value.Task;
            Assert.Contains("FunctionsNetHost.exe", placeholderChannel.WorkerProcess.Process.StartInfo.FileName);
            Assert.NotNull(placeholderChannel.WorkerProcess.Process.Id);
            var runningProcess = Process.GetProcessById(placeholderChannel.WorkerProcess.Id);
            Assert.Contains(runningProcess.ProcessName, "FunctionsNetHost");

            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteContainerReady, "1");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");

            response = await client.GetAsync("api/HttpRequestDataFunction");
            response.EnsureSuccessStatusCode();

            // Placeholder hit; these should match
            var specializedChannel = await webChannelManager.GetChannels("dotnet-isolated").Single().Value.Task;
            Assert.Same(placeholderChannel, specializedChannel);
            runningProcess = Process.GetProcessById(placeholderChannel.WorkerProcess.Id);
            Assert.Contains(runningProcess.ProcessName, "FunctionsNetHost");

            var log = _loggerProvider.GetLog();
            Assert.Contains("UsePlaceholderDotNetIsolated: True", log);
            Assert.Contains("Placeholder runtime version: '6.0'. Site runtime version: '6.0'. Match: True", log);
            Assert.DoesNotContain("Shutting down placeholder worker.", log);
        }

        [Fact]
        public async Task DotNetIsolated_PlaceholderHit_WithProxies()
        {
            // This test ensures that capabilities are correctly applied in EnvironmentReload during
            // specialization
            var builder = InitializeDotNetIsolatedPlaceholderBuilder(_dotnetIsolated60Path, "HttpRequestFunction");

            using var testServer = new TestServer(builder);

            var client = testServer.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            var response = await client.GetAsync("api/warmup");
            response.EnsureSuccessStatusCode();

            // Validate that the channel is set up with native worker
            var webChannelManager = testServer.Services.GetService<IWebHostRpcWorkerChannelManager>();

            var placeholderChannel = await webChannelManager.GetChannels("dotnet-isolated").Single().Value.Task;
            Assert.Contains("FunctionsNetHost.exe", placeholderChannel.WorkerProcess.Process.StartInfo.FileName);
            Assert.NotNull(placeholderChannel.WorkerProcess.Process.Id);
            var runningProcess = Process.GetProcessById(placeholderChannel.WorkerProcess.Id);
            Assert.Contains(runningProcess.ProcessName, "FunctionsNetHost");

            // This has to be on the actual environment in order to propagate to worker
            using var proxyEnv = new TestScopedEnvironmentVariable("UseProxyInTest", "1");

            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteContainerReady, "1");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");

            Task<HttpResponseMessage> responseTask = client.GetAsync("api/HttpRequestFunction");

            // Cancellation not working with TestServer
            await TestHelpers.Await(() => responseTask.IsCompleted, timeout: 5000);

            response = await responseTask;
            response.EnsureSuccessStatusCode();

            // Placeholder hit; these should match
            var specializedChannel = await webChannelManager.GetChannels("dotnet-isolated").Single().Value.Task;
            Assert.Same(placeholderChannel, specializedChannel);
            runningProcess = Process.GetProcessById(placeholderChannel.WorkerProcess.Id);
            Assert.Contains(runningProcess.ProcessName, "FunctionsNetHost");

            var log = _loggerProvider.GetLog();
            Assert.Contains("UsePlaceholderDotNetIsolated: True", log);
            Assert.Contains("Placeholder runtime version: '6.0'. Site runtime version: '6.0'. Match: True", log);
            Assert.DoesNotContain("Shutting down placeholder worker.", log);
        }

        [Fact]
        public async Task DotNetIsolated_PlaceholderMiss_EnvVar()
        {
            // Placeholder miss if the WEBSITE_USE_PLACEHOLDER_DOTNETISOLATED env var is not set
            await DotNetIsolatedPlaceholderMiss(_dotnetIsolated60Path);

            var log = _loggerProvider.GetLog();
            Assert.Contains("UsePlaceholderDotNetIsolated: False", log);
            Assert.Contains("Shutting down placeholder worker. Worker is not compatible for runtime: dotnet-isolated", log);
        }

        [Fact]
        public async Task DotNetIsolated_PlaceholderMiss_Not64Bit()
        {
            _environment.SetProcessBitness(is64Bitness: false);

            // We only specialize when host process is 64 bit process.
            await DotNetIsolatedPlaceholderMiss(_dotnetIsolated60Path, () =>
            {
                _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteUsePlaceholderDotNetIsolated, "1");
                _environment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeVersionSettingName, "6.0");
            });

            var log = _loggerProvider.GetLog();
            Assert.Contains("UsePlaceholderDotNetIsolated: True", log);
            Assert.Contains("This app is configured as 32-bit and therefore does not leverage all performance optimizations. See https://aka.ms/azure-functions/dotnet/placeholders for more information.", log);
            Assert.Contains("Shutting down placeholder worker. Worker is not compatible for runtime: dotnet-isolated", log);
        }

        [Fact]
        public async Task DotNetIsolated_PlaceholderMiss_DotNetVer()
        {
            // Even with placeholders enabled via the WEBSITE_USE_PLACEHOLDER_DOTNETISOLATED env var,
            // if the dotnet version does not match, we should not use the placeholder
            await DotNetIsolatedPlaceholderMiss(_dotnetIsolated60Path, () =>
            {
                _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteUsePlaceholderDotNetIsolated, "1");
                _environment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeVersionSettingName, "7.0");
            });

            var log = _loggerProvider.GetLog();
            Assert.Contains("UsePlaceholderDotNetIsolated: True", log);
            Assert.Contains("Placeholder runtime version: '6.0'. Site runtime version: '7.0'. Match: False", log);
            Assert.Contains("Shutting down placeholder worker. Worker is not compatible for runtime: dotnet-isolated", log);
        }

        [Fact]
        public async Task DotNetIsolated_PlaceholderMiss_UnsupportedWorkerPackage()
        {
            await DotNetIsolatedPlaceholderMiss(_dotnetIsolatedUnsuppportedPath, () =>
            {
                _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteUsePlaceholderDotNetIsolated, "1");
                _environment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeVersionSettingName, "6.0");
            });

            var log = _loggerProvider.GetLog();
            Assert.Contains("UsePlaceholderDotNetIsolated: True", log);
            Assert.Contains("Placeholder runtime version: '6.0'. Site runtime version: '6.0'. Match: True", log);
            Assert.Contains("Shutting down placeholder worker. Worker is not compatible for runtime: dotnet-isolated", log);
        }

        [Fact]
        public async Task DotNetIsolated_PlaceholderMiss_EmptyScriptRoot()
        {
            await DotNetIsolatedPlaceholderMiss(_dotnetIsolatedEmptyScriptRoot, () =>
            {
                _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteUsePlaceholderDotNetIsolated, "1");
                _environment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeVersionSettingName, "6.0");
            });

            var log = _loggerProvider.GetLog();
            Assert.Contains("UsePlaceholderDotNetIsolated: True", log);
            Assert.Contains("Placeholder runtime version: '6.0'. Site runtime version: '6.0'. Match: True", log);
            Assert.Contains("Shutting down placeholder worker. Worker is not compatible for runtime: dotnet-isolated", log);
        }

        [Fact]
        // Fix for https://github.com/Azure/azure-functions-host/issues/9288 
        public async Task SpecializedSite_StopsHostBeforeWorker()
        {
            // this app has a QueueTrigger reading from "myqueue-items"
            // add a few messages there before stopping the host
            var storageValue = TestHelpers.GetTestConfiguration().GetWebJobsConnectionString("AzureWebJobsStorage");
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(storageValue);
            CloudQueueClient queueClient = storageAccount.CreateCloudQueueClient();
            CloudQueue queue = queueClient.GetQueueReference("myqueue-items");
            await queue.CreateIfNotExistsAsync();
            await queue.ClearAsync();

            var builder = InitializeDotNetIsolatedPlaceholderBuilder(_dotnetIsolated60Path, "HttpRequestDataFunction", "QueueFunction");

            using var testServer = new TestServer(builder);

            var client = testServer.CreateClient();
            var response = await client.GetAsync("api/warmup");
            response.EnsureSuccessStatusCode();

            _environment.SetEnvironmentVariable("AzureWebJobsStorage", storageValue);
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteContainerReady, "1");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");

            response = await client.GetAsync("api/HttpRequestDataFunction");
            response.EnsureSuccessStatusCode();

            var scriptHostManager = testServer.Services.GetService<IScriptHostManager>();

            scriptHostManager.ActiveHostChanged += (object sender, ActiveHostChangedEventArgs e) =>
            {
                // for this test, this signals the host is about to shut down, so introduce an
                // intentional delay to simulate a race condition
                //
                // there was a bug where we'd stop the worker channel and process before the host, resulting in
                // a lot of "Did not find initialized language worker" errors due to a race between the process
                // and listeners shutting down                
                if (e.NewHost == null)
                {
                    Thread.Sleep(1000);
                }
            };

            bool keepRunning = true;

            Task messageTask = Task.Run(async () =>
            {
                while (keepRunning)
                {
                    await queue.AddMessageAsync(new CloudQueueMessage("test"));
                }
            });

            // make sure the invocations are flowing before we stop the host
            await TestHelpers.Await(() =>
            {
                int completed = _loggerProvider.GetAllLogMessages().Count(p => p.Category == "Function.QueueFunction" && p.EventId.Name == "FunctionCompleted");
                return completed > 10;
            });

            await testServer.Host.StopAsync();

            keepRunning = false;
            await messageTask;
            await queue.ClearAsync();

            var completedLogs = _loggerProvider.GetAllLogMessages()
                .Where(p => p.Category == "Function.QueueFunction")
                .Where(p => p.EventId.Name == "FunctionCompleted");

            Assert.NotEmpty(completedLogs.Where(p => p.Level == LogLevel.Information));
            Assert.Empty(completedLogs.Where(p => p.Level == LogLevel.Error));
        }

        private async Task DotNetIsolatedPlaceholderMiss(string scriptRootPath, Action additionalSpecializedSetup = null)
        {
            var builder = InitializeDotNetIsolatedPlaceholderBuilder(scriptRootPath, "HttpRequestDataFunction");

            // remove WEBSITE_USE_PLACEHOLDER_DOTNETISOLATED
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteUsePlaceholderDotNetIsolated, null);

            using var testServer = new TestServer(builder);

            var client = testServer.CreateClient();
            var response = await client.GetAsync("api/warmup");
            response.EnsureSuccessStatusCode();

            // Validate that the channel is set up with native worker
            var webChannelManager = testServer.Services.GetService<IWebHostRpcWorkerChannelManager>();

            var placeholderChannel = await webChannelManager.GetChannels("dotnet-isolated").Single().Value.Task;
            Assert.Contains("FunctionsNetHost.exe", placeholderChannel.WorkerProcess.Process.StartInfo.FileName);
            Assert.NotNull(placeholderChannel.WorkerProcess.Process.Id);
            var runningProcess = Process.GetProcessById(placeholderChannel.WorkerProcess.Id);
            Assert.Contains(runningProcess.ProcessName, "FunctionsNetHost");

            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteContainerReady, "1");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");

            additionalSpecializedSetup?.Invoke();

            response = await client.GetAsync("api/HttpRequestDataFunction");
            if (scriptRootPath == _dotnetIsolatedEmptyScriptRoot)
            {
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            }
            else
            {
                response.EnsureSuccessStatusCode();

                var expectedProcessName = scriptRootPath == _dotnetIsolated60Path ? "DotNetIsolated60" : "DotNetIsolatedUnsupported";
                // Placeholder miss; new channel should be started using the deployed worker directly
                var specializedChannel = await webChannelManager.GetChannels("dotnet-isolated").Single().Value.Task;
                Assert.Contains("dotnet.exe", specializedChannel.WorkerProcess.Process.StartInfo.FileName);
                Assert.Contains(expectedProcessName, specializedChannel.WorkerProcess.Process.StartInfo.Arguments);
                runningProcess = Process.GetProcessById(specializedChannel.WorkerProcess.Id);
                Assert.Contains(runningProcess.ProcessName, "dotnet");

                // Ensure other process is gone.
                Assert.DoesNotContain(Process.GetProcesses(), p => p.ProcessName.Contains("FunctionsNetHost"));
                Assert.Throws<InvalidOperationException>(() => placeholderChannel.WorkerProcess.Process.Id);
            }
        }

        private static void BuildDotnetIsolated60()
        {
            var p = Process.Start("dotnet", $"build {_dotnetIsolated60Path}/../../..");
            p.WaitForExit();
        }

        private IWebHostBuilder InitializeDotNetIsolatedPlaceholderBuilder(string scriptRootPath, params string[] functions)
        {
            BuildDotnetIsolated60();

            _environment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName, "dotnet-isolated");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteUsePlaceholderDotNetIsolated, "1");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, ScriptConstants.FeatureFlagEnableWorkerIndexing);
            _environment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeVersionSettingName, "6.0");

            var builder = CreateStandbyHostBuilder(functions);

            builder.ConfigureAppConfiguration(config =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string>
                {
                    { _scriptRootConfigPath, scriptRootPath },
                });
            });

            return builder;
        }

        private IWebHostBuilder CreateStandbyHostBuilder(params string[] functions)
        {
            var builder = Program.CreateWebHostBuilder()
                .ConfigureLogging(b =>
                {
                    b.AddProvider(_loggerProvider);
                    b.AddFilter<TestLoggerProvider>("Microsoft.Azure.WebJobs", LogLevel.Debug);
                    b.AddFilter<TestLoggerProvider>("Worker", LogLevel.Debug);
                })
                .ConfigureAppConfiguration(c =>
                {
                    c.AddInMemoryCollection(new Dictionary<string, string>
                    {
                        { _scriptRootConfigPath, _specializedScriptRoot }
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
                    s.AddLogging(logging =>
                    {
                        logging.AddProvider(_loggerProvider);
                    });

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