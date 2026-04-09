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
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Host.Scale;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Logging.ApplicationInsights;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Xunit;
using Xunit.Abstractions;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    [Trait(TestTraits.Group, TestTraits.NonE2ESpecialization)]
    public class SpecializationE2ETests
    {
        private static readonly SemaphoreSlim _pauseBeforeHostBuild = new(1, 1);
        private static readonly SemaphoreSlim _pauseAfterStandbyHostBuild = new(1, 1);
        private static readonly SemaphoreSlim _buildCount = new(2, 2);

        private static readonly string _standbyPath = Path.Combine(Path.GetTempPath(), "functions", "standby", "wwwroot");
        private static readonly string _scriptRootConfigPath = ConfigurationPath.Combine(ConfigurationSectionNames.WebHost, nameof(ScriptApplicationHostOptions.ScriptPath));
        private static readonly string _logPathConfigPath = ConfigurationPath.Combine(ConfigurationSectionNames.WebHost, nameof(ScriptApplicationHostOptions.LogPath));
        private static readonly string _testLogPath = Path.Combine(Path.GetTempPath(), "Functions", "SpecializationE2ETests");

        private static readonly string _dotnetIsolated60Path = Path.GetFullPath($@"..\..\DotNetIsolated60\{TestHelpers.BuildConfig}");
        private static readonly string _dotnetIsolatedUnsuppportedPath = Path.GetFullPath($@"..\..\DotNetIsolatedUnsupportedWorker\{TestHelpers.BuildConfig}");
        private static readonly string _dotnetIsolatedEmptyScriptRoot = Path.GetFullPath(@"..\..\..\..\EmptyScriptRoot");
        private static readonly string _dotnetCustomHandlerPath = Path.GetFullPath($@"..\..\DotNetCustomHandler\{TestHelpers.BuildConfig}");
        private static readonly string _dotnetIsolatedWithBundlesPath = Path.GetFullPath($@"..\..\DotNetIsolatedWithBundles\{TestHelpers.BuildConfig}");
        private static readonly string _customHandlerWithBundlesPath = Path.GetFullPath(@"..\..\..\..\sample\CustomHandler");

        private static Action<IServiceCollection> _customizeScriptHostServices;

        private const string _specializedScriptRoot = @"TestScripts\CSharp";

        private readonly TestEnvironment _environment;
        private readonly TestLoggerProvider _loggerProvider;
        private readonly TestMetricsLogger _testMetricsLogger = new();

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

            _testOutputHelper = testOutputHelper;

            // allow each test to override this
            _customizeScriptHostServices = null;
        }

        [Fact]
        public async Task ApplicationInsights_InvocationsContainDifferentOperationIds()
        {
            // Verify that when a request specializes the host we don't capture the context
            // of that request. Application Insights uses this context to correlate telemetry
            // so it had a confusing effect. Previously all TimerTrigger traces would have the
            // operation id of this request and all host logs would as well.
            var channel = new TestTelemetryChannel();

            var builder = CreateStandbyHostBuilder(_loggerProvider, "OneSecondTimer", "FunctionExecutionContext")
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

            await using var stoppable = new StoppableHost(builder.Build());

            var webHost = stoppable.Inner;

            await webHost.StartAsync();

            var client = webHost.GetTestClient();


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

        [Fact]
        public async Task Specialization_ThreadUtilization()
        {
            var builder = CreateStandbyHostBuilder(_loggerProvider, "FunctionExecutionContext");

            await using var stoppable = new StoppableHost(builder.Build());

            var host = stoppable.Inner;

            await host.StartAsync();

            var client = host.GetTestClient();

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

        [Fact]
        public async Task Specialization_WebHostOptionsAreLogged()
        {
            const string optionsCategory = "Microsoft.Azure.WebJobs.Hosting.OptionsLoggingService";

            var builder = CreateStandbyHostBuilder(_loggerProvider, "FunctionExecutionContext");

            await using var stoppable = new StoppableHost(builder.Build());

            var host = stoppable.Inner;

            await host.StartAsync();

            var client = host.GetTestClient();

            var response = await client.GetAsync("api/warmup");
            response.EnsureSuccessStatusCode();

            // Specialize
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteContainerReady, "1");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");

            response = await client.GetAsync("api/functionexecutioncontext");
            response.EnsureSuccessStatusCode();

            // Wait for the OptionsLoggingService to process all queued log entries.
            await TestHelpers.Await(() =>
                _loggerProvider.GetAllLogMessages()
                    .Any(m => string.Equals(m.Category, optionsCategory, StringComparison.Ordinal)
                           && m.FormattedMessage.StartsWith(nameof(ScriptJobHostOptions), StringComparison.Ordinal)));

            var allOptionsLogs = _loggerProvider.GetAllLogMessages()
                .Where(m => string.Equals(m.Category, optionsCategory, StringComparison.Ordinal))
                .Select(m => m.FormattedMessage)
                .ToList();

            // WebHost-level options implementing IOptionsFormatter should be logged.
            TestHelpers.AssertOptionLogged(allOptionsLogs, nameof(HttpBodyControlOptions));
            TestHelpers.AssertOptionLogged(allOptionsLogs, nameof(ResponseCompressionOptions));
            TestHelpers.AssertOptionLogged(allOptionsLogs, nameof(HostHealthMonitorOptions));
            TestHelpers.AssertOptionLogged(allOptionsLogs, nameof(WorkerConfigurationResolverOptions));
            TestHelpers.AssertOptionLogged(allOptionsLogs, nameof(LanguageWorkerOptions));

            // ScriptHost-level options should also be present.
            TestHelpers.AssertOptionLogged(allOptionsLogs, nameof(ScriptJobHostOptions));
            TestHelpers.AssertOptionLogged(allOptionsLogs, nameof(FunctionResultAggregatorOptions));
            TestHelpers.AssertOptionLogged(allOptionsLogs, nameof(ConcurrencyOptions));
            TestHelpers.AssertOptionLogged(allOptionsLogs, nameof(SingletonOptions));
            TestHelpers.AssertOptionLogged(allOptionsLogs, nameof(ScaleOptions));
            TestHelpers.AssertOptionLogged(allOptionsLogs, nameof(HttpOptions));
        }

        [Fact]
        public async Task Specialization_ResetsSharedLoadContext()
        {
            var builder = CreateStandbyHostBuilder(_loggerProvider, "FunctionExecutionContext");

            await using var stoppable = new StoppableHost(builder.Build());

            var host = stoppable.Inner;

            await host.StartAsync();

            var client = host.GetTestClient();

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

        [Fact]
        public async Task ForNonReadOnlyFileSystem_RestartWorkerForSpecializationAndHotReload()
        {
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, "node");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, ScriptConstants.FeatureFlagEnableWorkerIndexing);

            var builder = CreateStandbyHostBuilder(_loggerProvider, "HttpTriggerNoAuth");

            builder.ConfigureAppConfiguration(config =>
            {
                string scriptRootConfigPath = ConfigurationPath.Combine(ConfigurationSectionNames.WebHost, nameof(ScriptApplicationHostOptions.ScriptPath));
                config.AddInMemoryCollection(new Dictionary<string, string>
                {
                    { _scriptRootConfigPath, Path.GetFullPath(@"TestScripts\NodeWithBundles") }
                });
            });

            await using var stoppable = new StoppableHost(builder.Build());

            var host = stoppable.Inner;

            await host.StartAsync();

            var client = host.GetTestClient();

            var response = await client.GetAsync("api/warmup");
            response.EnsureSuccessStatusCode();

            var webChannelManager = host.Services.GetService<IWebHostRpcWorkerChannelManager>();
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

            var manager = host.Services.GetService<IScriptHostManager>();
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
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, "node");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, ScriptConstants.FeatureFlagEnableWorkerIndexing);

            var builder = CreateStandbyHostBuilder(_loggerProvider, "HttpTriggerNoAuth");

            builder.ConfigureAppConfiguration(config =>
            {
                string scriptRootConfigPath = ConfigurationPath.Combine(ConfigurationSectionNames.WebHost, nameof(ScriptApplicationHostOptions.ScriptPath));
                config.AddInMemoryCollection(new Dictionary<string, string>
                {
                    { _scriptRootConfigPath, Path.GetFullPath(@"TestScripts\NodeWithBundles") }
                });
            });

            await using var stoppable = new StoppableHost(builder.Build());

            var host = stoppable.Inner;

            await host.StartAsync();

            var client = host.GetTestClient();

            var response = await client.GetAsync("api/warmup");
            response.EnsureSuccessStatusCode();

            var placeholderContext = FunctionAssemblyLoadContext.Shared;

            var webChannelManager = host.Services.GetService<IWebHostRpcWorkerChannelManager>();
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
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, "node");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, ScriptConstants.FeatureFlagEnableWorkerIndexing);

            var builder = CreateStandbyHostBuilder(_loggerProvider, "HttpTriggerNoAuth");
            string isFileSystemReadOnly = ConfigurationPath.Combine(ConfigurationSectionNames.WebHost, nameof(ScriptApplicationHostOptions.IsFileSystemReadOnly));

            builder.ConfigureAppConfiguration(config =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string>
                    {
                        { _scriptRootConfigPath, Path.GetFullPath(@"TestScripts\NodeWithBundles") }
                    });
                });

            await using var stoppable = new StoppableHost(builder.Build());

            var host = stoppable.Inner;

            await host.StartAsync();

            var client = host.GetTestClient();

            var response = await client.GetAsync("api/warmup");
            response.EnsureSuccessStatusCode();

            var webChannelManager = host.Services.GetService<IWebHostRpcWorkerChannelManager>();
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
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, "node");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, ScriptConstants.FeatureFlagEnableWorkerIndexing);

            var builder = CreateStandbyHostBuilder(_loggerProvider, "HttpTriggerNoAuth");
            string isFileSystemReadOnly = ConfigurationPath.Combine(ConfigurationSectionNames.WebHost, nameof(ScriptApplicationHostOptions.IsFileSystemReadOnly));

            builder.ConfigureAppConfiguration(config =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string>
                {
                    { _scriptRootConfigPath, Path.GetFullPath(@"TestScripts\NodeWithBundles") }
                });
            });

            await using var stoppable = new StoppableHost(builder.Build());

            var host = stoppable.Inner;

            await host.StartAsync();

            var client = host.GetTestClient();

            var response = await client.GetAsync("api/warmup");
            response.EnsureSuccessStatusCode();

            var webChannelManager = host.Services.GetService<IWebHostRpcWorkerChannelManager>();
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

            AssertWorkerProcessStartupCount(2);

            AssertLanguageWorkerOptionsSetupCount(2);
        }

        [Fact]
        // This test doesn't specialize, but is here to compare behavior with the other Arguments test above
        public async Task NoSpecialization_StartWorkerWithWorkerArguments()
        {
            _environment.Clear();

            _environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, "node");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, ScriptConstants.FeatureFlagEnableWorkerIndexing);

            var builder = CreateStandbyHostBuilder(_loggerProvider, "HttpTriggerNoAuth");

            builder.ConfigureAppConfiguration(config =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string>
                {
                    { _scriptRootConfigPath, Path.GetFullPath(@"TestScripts\NodeWithBundles") }
                });
            });

            // Use an actual env var here as it will be refreshed in config after specialization
            using var envVars = new TestScopedEnvironmentVariable("languageWorkers:node:arguments", "--max-old-space-size=1272");

            await using var stoppable = new StoppableHost(builder.Build());

            var host = stoppable.Inner;

            await host.StartAsync();

            var client = host.GetTestClient();

            var response = await client.GetAsync("api/HttpTriggerNoAuth");
            response.EnsureSuccessStatusCode();

            var webChannelManager = host.Services.GetService<IWebHostRpcWorkerChannelManager>();
            var channel = await webChannelManager.GetChannels("node").Single().Value.Task;
            Assert.Contains("--max-old-space-size=1272", channel.WorkerProcess.Process.StartInfo.Arguments);

            AssertWorkerProcessStartupCount(1);

            AssertLanguageWorkerOptionsSetupCount(1);
        }

        [Fact]
        public async Task Specialization_GCMode()
        {
            var builder = CreateStandbyHostBuilder(_loggerProvider, "FunctionExecutionContext");

            await using var stoppable = new StoppableHost(builder.Build());

            var host = stoppable.Inner;

            await host.StartAsync();

            var client = host.GetTestClient();

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

        [Fact]
        public async Task Specialization_ResetsSecretManagerRepository()
        {
            var builder = CreateStandbyHostBuilder(_loggerProvider, "FunctionExecutionContext")
                .ConfigureLogging(logging =>
                {
                    logging.AddFilter<TestLoggerProvider>(null, LogLevel.Debug);
                });

            await using var stoppable = new StoppableHost(builder.Build());

            var host = stoppable.Inner;

            await host.StartAsync();

            var client = host.GetTestClient();

            var response = await client.GetAsync("api/warmup");
            response.EnsureSuccessStatusCode();

            var provider = host.Services.GetService<ISecretManagerProvider>();
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

        [Fact]
        public async Task StartAsync_SetsCorrectActiveHost_RefreshesLanguageWorkerOptions()
        {
            var builder = CreateStandbyHostBuilder(_loggerProvider);

            await _pauseAfterStandbyHostBuild.WaitAsync();

            // We want it to start first, but finish last, so unstick it in a couple seconds.
            Task ignore = Task.Delay(3000).ContinueWith(_ => _pauseAfterStandbyHostBuild.Release());

            var expectedPowerShellVersion = "7.4";
            await using var stoppable = new StoppableHost(builder.Build());
            var host = stoppable.Inner;

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
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, "powershell");
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
                var builder = CreateStandbyHostBuilder(_loggerProvider, "FunctionExecutionContext")
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

                await using var stoppable = new StoppableHost(builder.Build());

                var host = stoppable.Inner;

                await host.StartAsync();

                var client = host.GetTestClient();

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
                var builder = CreateStandbyHostBuilder(_loggerProvider, "FunctionExecutionContext")
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

                await using var stoppable = new StoppableHost(builder.Build());

                var host = stoppable.Inner;

                await host.StartAsync();

                var client = host.GetTestClient();

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

        [Fact]
        public async Task Specialization_CustomStartupAddsWebJobsStorage()
        {
            var storageValue = TestHelpers.GetTestConfiguration().GetWebJobsConnectionString("AzureWebJobsStorage");

            // No AzureWebJobsStorage set in environment variables (App Settings from portal)
            using (new TestScopedEnvironmentVariable("AzureWebJobsStorage", ""))
            {
                var builder = CreateStandbyHostBuilder(_loggerProvider, "FunctionExecutionContext")
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

                await using var stoppable = new StoppableHost(builder.Build());

                var host = stoppable.Inner;

                await host.StartAsync();

                var client = host.GetTestClient();

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
            using (new TestScopedEnvironmentVariable("AzureFunctionsJobHost__InternalSasBlobContainer", null))
            using (new TestScopedEnvironmentVariable("AzureWebJobsStorage", null))
            {
                var builder = CreateStandbyHostBuilder(_loggerProvider, "FunctionExecutionContext")
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

                await using var stoppable = new StoppableHost(builder.Build());

                var host = stoppable.Inner;

                await host.StartAsync();

                var client = host.GetTestClient();

                var response = await client.GetAsync("api/warmup");
                response.EnsureSuccessStatusCode();

                // Should not be able to get the Hosting BlobContainerClient before specialization since
                // customer provided storage-related configuration is not in the Environment
                var blobStorageProvider = host.Services.GetService<IAzureBlobStorageProvider>();
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
                    blobStorageProvider = host.Services.GetService<IAzureBlobStorageProvider>();
                    Assert.True(blobStorageProvider.TryCreateHostingBlobContainerClient(out var blobContainerClient));
                    Assert.Equal("test-sas-container", blobContainerClient.Name);
                }
                await containerClient.DeleteAsync();
            }
        }

        [Theory]
        [InlineData(ScriptConstants.FlexConsumptionSku, ScriptConstants.FeatureFlagEnableMcpCustomHandlerPreview, true)]
        [InlineData(ScriptConstants.FlexConsumptionSku, $"Feature1,{ScriptConstants.FeatureFlagEnableMcpCustomHandlerPreview}", true)]
        [InlineData(ScriptConstants.FlexConsumptionSku, "Feature1", false)]
        [InlineData(ScriptConstants.FlexConsumptionSku, null, false)]
        [InlineData(ScriptConstants.DynamicSku, null, false)]
        [InlineData(ScriptConstants.DynamicSku, ScriptConstants.FeatureFlagEnableMcpCustomHandlerPreview, false)]
        [InlineData(ScriptConstants.DynamicSku, $"Feature1,{ScriptConstants.FeatureFlagEnableMcpCustomHandlerPreview}", false)]
        [InlineData(ScriptConstants.ElasticPremiumSku, null, false)]
        [InlineData(ScriptConstants.ElasticPremiumSku, $"Feature1,{ScriptConstants.FeatureFlagEnableMcpCustomHandlerPreview}", false)]
        [InlineData("", null, false)]
        public async Task Specialization_FlexSku_McpPreview_SetsWorkerRuntimeToCustom(string websiteSku, string featureFlags, bool isExpectedToResetWorkerRuntime)
        {
            var environmentVariables = new Dictionary<string, string>
            {
                { EnvironmentSettingNames.AzureWebsiteSku, websiteSku }
            };
            var builder = InitializeDotNetIsolatedPlaceholderBuilder(_dotnetCustomHandlerPath, _loggerProvider, environmentVariables, "SimpleHttpTrigger");

            await using var stoppable = new StoppableHost(builder.Build());

            var host = stoppable.Inner;

            await host.StartAsync();

            var client = host.GetTestClient();
            var response = await client.GetAsync("api/warmup");
            response.EnsureSuccessStatusCode();

            // Validate that the channel is set up with native worker
            var webChannelManager = host.Services.GetService<IWebHostRpcWorkerChannelManager>();
            var placeholderChannel = await webChannelManager.GetChannels("dotnet-isolated").Single().Value.Task;
            Assert.Contains("FunctionsNetHost.exe", placeholderChannel.WorkerProcess.Process.StartInfo.FileName);
            Assert.NotNull(placeholderChannel.WorkerProcess.Process.Id);

            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, featureFlags);
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteContainerReady, "1");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");

            response = await client.GetAsync("api/SimpleHttpTrigger");
            response.EnsureSuccessStatusCode();

            var log = _loggerProvider.GetLog();

            if (isExpectedToResetWorkerRuntime)
            {
                // Verify expected logs when running the custom handler executable.
                Assert.Contains("MCP custom handler preview is enabled. Setting FUNCTIONS_WORKER_RUNTIME to 'custom'", log);
                Assert.Contains("Mapped function route 'api/SimpleHttpTrigger'", log);
            }
            else
            {
                Assert.DoesNotContain("MCP custom handler preview is enabled. Setting FUNCTIONS_WORKER_RUNTIME to 'custom'", log);
            }
        }

        [Fact]
        public async Task DotNetIsolated_PlaceholderHit()
        {
            var builder = InitializeDotNetIsolatedPlaceholderBuilder(_dotnetIsolated60Path, _loggerProvider, "HttpRequestDataFunction");

            await using var stoppable = new StoppableHost(builder.Build());

            var host = stoppable.Inner;

            await host.StartAsync();

            var client = host.GetTestClient();
            var response = await client.GetAsync("api/warmup");
            response.EnsureSuccessStatusCode();

            // Validate that the channel is set up with native worker
            var webChannelManager = host.Services.GetService<IWebHostRpcWorkerChannelManager>();

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

            AssertWorkerProcessStartupCount(1);

            // because placeholder app has bundles in host.json, it refreshes 2x before specializing.
            AssertLanguageWorkerOptionsSetupCount(3);
        }

        [Fact]
        public async Task DotNetIsolatedWithBundles_PlaceholderHit()
        {
            var builder = InitializeDotNetIsolatedPlaceholderBuilder(_dotnetIsolatedWithBundlesPath, _loggerProvider, "HttpRequestFunction");

            await using var stoppable = new StoppableHost(builder.Build());

            var host = stoppable.Inner;

            await host.StartAsync();

            var client = host.GetTestClient();
            var response = await client.GetAsync("api/warmup");
            response.EnsureSuccessStatusCode();

            // Validate that the channel is set up with native worker
            var webChannelManager = host.Services.GetService<IWebHostRpcWorkerChannelManager>();

            var placeholderChannel = await webChannelManager.GetChannels("dotnet-isolated").Single().Value.Task;
            Assert.Contains("FunctionsNetHost.exe", placeholderChannel.WorkerProcess.Process.StartInfo.FileName);
            Assert.NotNull(placeholderChannel.WorkerProcess.Process.Id);
            var runningProcess = Process.GetProcessById(placeholderChannel.WorkerProcess.Id);
            Assert.Contains(runningProcess.ProcessName, "FunctionsNetHost");

            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteContainerReady, "1");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");

            response = await client.GetAsync("api/HttpRequestFunction");
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

            AssertWorkerProcessStartupCount(1);

            // because placeholder app has bundles in host.json, it refreshes 2x before specializing.
            // and then it refreshes for bundles and for dotnet-isolated when specializing.
            // This is not really a valid scenario, but we need to ensure it keeps working. Customers should not use
            // bundles with dotnet-isolated.
            AssertLanguageWorkerOptionsSetupCount(4);
        }

        /// <summary>
        /// Regression test for https://github.com/Azure/azure-functions-host/issues/11676.        
        /// </summary>
        [Fact]
        public async Task Specialization_DotNetIsolatedToCustomHandler_BundleExtensionsLoadCorrectly()
        {
            // Forces download of bundles.            
            string downloadPath = $"{ConfigurationSectionNames.JobHost}__{ConfigurationSectionNames.ExtensionBundle}__DownloadPath";
            using var path = new TestScopedEnvironmentVariable(downloadPath, Path.Combine(Path.GetTempPath(), "BundlesTests"));
            using var coreTools = new TestScopedEnvironmentVariable(EnvironmentSettingNames.CoreToolsEnvironment, "1");

            // Start in placeholder mode as dotnet-isolated
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, "dotnet-isolated");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, ScriptConstants.FeatureFlagEnableWorkerIndexing);

            var builder = CreateStandbyHostBuilder(_loggerProvider, "HttpTrigger", "ServiceBusTriggerFunction", "EventHubTriggerFunction");

            // Override the specialized script root to point to our custom handler app with bundles
            builder.ConfigureAppConfiguration(config =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string>
                {
                    { _scriptRootConfigPath, _customHandlerWithBundlesPath },
                });
            });

            await using var stoppable = new StoppableHost(builder.Build());
            var webHost = stoppable.Inner;
            await webHost.StartAsync();
            var client = webHost.GetTestClient();

            // Warm up the placeholder
            var response = await client.GetAsync("api/warmup");
            Assert.True(response.IsSuccessStatusCode, _loggerProvider.GetLog());

            // Specialize into a custom handler app
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, "custom");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteContainerReady, "1");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");


            // Issue a request to trigger specialization. The custom handler won't respond, but
            // this forces the host through the full specialization + extension loading flow.
            response = await client.GetAsync("api/warmup");

            // Wait for the host to settle after specialization
            await TestHelpers.Await(() =>
                    {
                        var log = _loggerProvider.GetLog();
                        // Wait until we see evidence that the specialized host has started
                        return log.Contains("Host started") || log.Contains("function is in error") || log.Contains("No job functions found");
                    }, timeout: 30000);

            var log = _loggerProvider.GetLog();

            // Verify the specialized host found our custom handler functions.
            Assert.Contains("3 functions found (Host)", log);

            // The critical check: after IsScriptFileDetermined runs, functions must NOT be excluded.
            // With the bug, we'd see "0 functions loaded" from the ScriptStartupTypeLocator call
            // because IsScriptFileDetermined excludes custom handler functions (no scriptFile).
            Assert.DoesNotContain("0 functions loaded", log);

            // The binding types from function.json must NOT be missing from the extension bundle.
            Assert.DoesNotContain("were not found in the configured extension bundle", log);
        }

        [Theory]
        [InlineData("gzip", "gzip")]
        [InlineData("br", "br")]
        [InlineData("gzip, deflate, br", "br")]  // Compression defaults to Brotli compression when the client supports it.
        [InlineData("", null)]
        public async Task ResponseCompressionWorksAfterSpecialization(string acceptEncodingRequestHeaderValue, string expectedContentEncodingResponseHeaderValue)
        {
            var builder = InitializeDotNetIsolatedPlaceholderBuilder(_dotnetIsolated60Path, _loggerProvider, "HttpRequestDataFunction");

            await using var stoppable = new StoppableHost(builder.Build());

            var host = stoppable.Inner;

            await host.StartAsync();

            var client = host.GetTestClient();

            client.DefaultRequestHeaders.Add("Accept-Encoding", acceptEncodingRequestHeaderValue);
            var response = await client.GetAsync("api/warmup");
            response.EnsureSuccessStatusCode();
            response.Content.Headers.TryGetValues("Content-Encoding", out var value);
            Assert.Null(value);

            // Validate that the channel is set up with native worker
            var webChannelManager = host.Services.GetService<IWebHostRpcWorkerChannelManager>();
            var placeholderChannel = await webChannelManager.GetChannels("dotnet-isolated").Single().Value.Task;

            // Ensure we are in placeholder mode
            Assert.Contains("FunctionsNetHost.exe", placeholderChannel.WorkerProcess.Process.StartInfo.FileName);

            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteContainerReady, "1");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, ScriptConstants.FeatureFlagEnableResponseCompression);

            response = await client.GetAsync("api/HttpRequestDataFunction");
            response.EnsureSuccessStatusCode();
            response.Content.Headers.TryGetValues("Content-Encoding", out value);
            Assert.Equal(expectedContentEncodingResponseHeaderValue, value?.First());
        }

        [Fact]
        public async Task Specialization_DotNetIsolatedApp_MissingAzureFunctionsDir_Logs()
        {
            Guid guid = Guid.NewGuid();
            string path = "test-path" + guid.ToString();

            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            string json = "{\r\n  \"version\": \"2.0\",\r\n  \"isDefaultHostConfig\": false\r\n}";
            File.WriteAllText(Path.Combine(path, "host.json"), json);

            var builder = InitializeDotNetIsolatedPlaceholderBuilder(path, _loggerProvider);

            await using var stoppable = new StoppableHost(builder.Build());

            var host = stoppable.Inner;

            await host.StartAsync();

            var standbyManager = host.Services.GetService<IStandbyManager>();
            Assert.NotNull(standbyManager);

            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteContainerReady, "1");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, "dotnet-isolated");
            SystemEnvironment.Instance.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");

            await standbyManager.SpecializeHostAsync();

            // Assert: Verify that the host has specialized
            var scriptHostManager = host.Services.GetService<IScriptHostManager>();
            Assert.NotNull(scriptHostManager);
            Assert.Equal(ScriptHostState.Running, scriptHostManager.State);

            await TestHelpers.Await(() =>
            {
                int completed = _loggerProvider.GetAllLogMessages().Count(p => p.FormattedMessage.Contains("Could not find the .azurefunctions folder in the deployed artifacts of a .NET isolated function app."));
                return completed > 0;
            });
        }

        [Fact]
        public async Task Specialization_DynamicResolution_FallbackPath_Logs()
        {
            var loggerProvider = new TestLoggerProvider();
            Guid guid = Guid.NewGuid();
            string path = "test-path" + guid.ToString();

            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            string json = "{\r\n  \"version\": \"2.0\",\r\n  \"isDefaultHostConfig\": false\r\n}";
            File.WriteAllText(Path.Combine(path, "host.json"), json);
            string fallbackPath = Path.Combine(Directory.GetCurrentDirectory(), "workers");

            var builder = InitializeDotNetIsolatedPlaceholderBuilder(path, loggerProvider);
            builder.ConfigureServices(services =>
            {
                services.Configure<FunctionsHostingConfigOptions>(o => o.Features["WORKERS_AVAILABLE_FOR_DYNAMIC_RESOLUTION"] = "node");
            });

            await using var stoppable = new StoppableHost(builder.Build());

            var host = stoppable.Inner;

            await host.StartAsync();

            var standbyManager = host.Services.GetService<IStandbyManager>();
            Assert.NotNull(standbyManager);

            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteContainerReady, "1");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, "node");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");

            var logs = loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage);

            Assert.Contains("Placeholder mode is enabled: True", logs);

            var nodeLog = logs.FirstOrDefault(p => p.Contains("Added WorkerConfig for language: node with worker path:") && p.Contains("workers\\node"));
            Assert.True(nodeLog.Any());

            var javaLog = logs.FirstOrDefault(p => p.Contains("Added WorkerConfig for language: java with worker path:") && p.Contains("workers\\java"));
            Assert.True(javaLog.Any());

            var probingLog = logs.FirstOrDefault(p => p.Contains("Worker probing paths set to:"));
            Assert.True(probingLog.Any());

            loggerProvider.ClearAllLogMessages();

            await standbyManager.SpecializeHostAsync();

            // Assert: Verify that the host has specialized
            var scriptHostManager = host.Services.GetService<IScriptHostManager>();
            Assert.NotNull(scriptHostManager);
            Assert.Equal(ScriptHostState.Running, scriptHostManager.State);

            var newLogs = loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage);

            Assert.Contains("Completed language worker channel specialization", newLogs);

            var newNodeLog = newLogs.FirstOrDefault(p => p.Contains("Added WorkerConfig for language: node with worker path:") && p.Contains("workers"));
            Assert.True(newNodeLog.Any());

            var newJavaLog = newLogs.FirstOrDefault(p => p.Contains("Added WorkerConfig for language: java with worker path:"));
            Assert.Null(newJavaLog);

            probingLog = logs.FirstOrDefault(p => p.Contains("Worker probing paths set to:"));
            Assert.True(probingLog.Any());
        }

        [Fact]
        public async Task Specialization_DynamicResolution_Logs()
        {
            var loggerProvider = new TestLoggerProvider();
            Guid guid = Guid.NewGuid();
            string path = "test-path" + guid.ToString();

            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            Guid guid2 = Guid.NewGuid();
            string workerPath = "worker-path" + guid2.ToString();

            if (!Directory.Exists(workerPath))
            {
                Directory.CreateDirectory(workerPath);
            }

            string subdir = Path.Combine(workerPath, "decoupledWorkers", "node", "1.0.0");

            if (!Directory.Exists(subdir))
            {
                Directory.CreateDirectory(subdir);
                string workerJson = @"{
                            ""description"": {
                                ""language"": ""node"",
                                ""extensions"": ["".js"", "".mjs"", "".cjs""],
                                ""defaultExecutablePath"": ""node"",
                                ""defaultWorkerPath"": ""worker.config.json"",
                                ""workerIndexing"": ""true""
                            },
                            ""hostRequirements"": []
                        }";

                File.WriteAllText(Path.Combine(subdir, "worker.config.json"), workerJson);
            }

            string json = "{\r\n  \"version\": \"2.0\",\r\n  \"isDefaultHostConfig\": false\r\n}";
            File.WriteAllText(Path.Combine(path, "host.json"), json);

            var builder = InitializeDotNetIsolatedPlaceholderBuilder(path, loggerProvider);
            var inMemorySettings = new Dictionary<string, string>();
            inMemorySettings["languageWorkers:probingPaths:0"] = Path.Combine(workerPath, "decoupledWorkers");

            builder.ConfigureServices(services =>
            {
                services.Configure<FunctionsHostingConfigOptions>(o => o.Features["WORKERS_AVAILABLE_FOR_DYNAMIC_RESOLUTION"] = "node");
            });

            builder.ConfigureAppConfiguration(c =>
            {
                c.AddInMemoryCollection(inMemorySettings);
            });

            await using var stoppable = new StoppableHost(builder.Build());

            var host = stoppable.Inner;

            await host.StartAsync();

            var standbyManager = host.Services.GetService<IStandbyManager>();
            Assert.NotNull(standbyManager);

            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteContainerReady, "1");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, "node");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");

            var logs = loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage);

            Assert.Contains("Placeholder mode is enabled: True", logs);

            var nodeLog = logs.FirstOrDefault(p => p.Contains("Added WorkerConfig for language: node with worker path:") && p.Contains("decoupledWorkers\\node"));
            Assert.True(nodeLog.Length != 0);

            var javaLog = logs.FirstOrDefault(p => p.Contains("Added WorkerConfig for language: java with worker path:") && p.Contains("workers\\java"));
            Assert.True(javaLog.Length != 0);

            var probingLog = logs.FirstOrDefault(p => p.Contains("Worker probing paths set to:"));
            Assert.True(probingLog.Length != 0);
        }

        [Fact]
        public async Task DotNetIsolated_PlaceholderHit_WithProxies()
        {
            // This test ensures that capabilities are correctly applied in EnvironmentReload during
            // specialization
            var builder = InitializeDotNetIsolatedPlaceholderBuilder(_dotnetIsolated60Path, _loggerProvider, "HttpRequestFunction");

            await using var stoppable = new StoppableHost(builder.Build());

            var host = stoppable.Inner;

            await host.StartAsync();

            var client = host.GetTestClient();

            client.Timeout = TimeSpan.FromSeconds(10);

            var response = await client.GetAsync("api/warmup");
            response.EnsureSuccessStatusCode();

            // Validate that the channel is set up with native worker
            var webChannelManager = host.Services.GetService<IWebHostRpcWorkerChannelManager>();

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

            AssertWorkerProcessStartupCount(2);

            // because placeholder app has bundles in host.json, it refreshes twice in placeholder mode for dotnet-isolated
            AssertLanguageWorkerOptionsSetupCount(3);
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

            var builder = InitializeDotNetIsolatedPlaceholderBuilder(_dotnetIsolated60Path, _loggerProvider, "HttpRequestDataFunction", "QueueFunction");

            await using var stoppable = new StoppableHost(builder.Build());

            var host = stoppable.Inner;

            await host.StartAsync();

            var client = host.GetTestClient();
            var response = await client.GetAsync("api/warmup");
            response.EnsureSuccessStatusCode();

            _environment.SetEnvironmentVariable("AzureWebJobsStorage", storageValue);
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteContainerReady, "1");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");

            response = await client.GetAsync("api/HttpRequestDataFunction");
            response.EnsureSuccessStatusCode();

            var scriptHostManager = host.Services.GetService<IScriptHostManager>();

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


            keepRunning = false;
            await messageTask;
            await queue.ClearAsync();

            var completedLogs = _loggerProvider.GetAllLogMessages()
                .Where(p => p.Category == "Function.QueueFunction")
                .Where(p => p.EventId.Name == "FunctionCompleted");

            Assert.NotEmpty(completedLogs.Where(p => p.Level == LogLevel.Information));
            Assert.Empty(completedLogs.Where(p => p.Level == LogLevel.Error));
        }

        [Fact]
        public async Task Specialization_Writes_WorkerStartupLogs()
        {
            // Create a test logger per-host so we can ensure logs go to the correct one when specializing
            var perScriptHostLoggers = new List<(bool IsPlaceholderMode, TestLoggerProvider Logger)>();
            _customizeScriptHostServices = s =>
            {
                var isPlaceholderMode = _environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode);
                var testLoggerProvider = new TestLoggerProvider();
                perScriptHostLoggers.Add(new(isPlaceholderMode == "1", testLoggerProvider));

                s.AddSingleton<ILoggerProvider>(testLoggerProvider);
            };

            var builder = InitializeDotNetIsolatedPlaceholderBuilder(_dotnetIsolated60Path, _loggerProvider, "HttpRequestDataFunction", "QueueFunction");
            var storageValue = TestHelpers.GetTestConfiguration().GetWebJobsConnectionString("AzureWebJobsStorage");

            await using var stoppable = new StoppableHost(builder.Build());

            var host = stoppable.Inner;

            await host.StartAsync();

            var client = host.GetTestClient();
            var response = await client.GetAsync("api/warmup");
            response.EnsureSuccessStatusCode();

            var webChannelManager = host.Services.GetService<IWebHostRpcWorkerChannelManager>();
            var placeholderChannel = await webChannelManager.GetChannels("dotnet-isolated").Single().Value.Task;
            var process = placeholderChannel.WorkerProcess as WorkerProcess;
            process.BuildAndLogConsoleLog("Fake console out from placeholder", LogLevel.Information);

            _environment.SetEnvironmentVariable("AzureWebJobsStorage", storageValue);
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteContainerReady, "1");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");

            response = await client.GetAsync("api/HttpRequestDataFunction");
            response.EnsureSuccessStatusCode();

            var placeholderLogger = perScriptHostLoggers.Single(p => p.IsPlaceholderMode).Logger;
            var userLogger = perScriptHostLoggers.Single(p => !p.IsPlaceholderMode).Logger;

            string logs = placeholderLogger.GetLog();

            Assert.Null(placeholderLogger.GetAllLogMessages().SingleOrDefault(p => p.Category == "Host.Function.Console"));
            var placeholderMessages = placeholderLogger.GetAllLogMessages().Select(p => p.FormattedMessage);
            Assert.DoesNotContain("Console Out from worker on startup.", placeholderMessages);
            Assert.DoesNotContain("Fake console out from placeholder", placeholderMessages); // placeholder 'user' console logs should never be logged

            Assert.Single(userLogger.GetAllLogMessages().Select(p => p.Category), "Host.Function.Console");
            var userMessages = userLogger.GetAllLogMessages().Select(p => p.FormattedMessage);
            Assert.Contains("Console Out from worker on startup.", userMessages);
            Assert.DoesNotContain("Fake console out from placeholder", userMessages); // this log should be 'lost' and never written
        }

        private async Task DotNetIsolatedPlaceholderMiss(string scriptRootPath, Action additionalSpecializedSetup = null)
        {
            var builder = InitializeDotNetIsolatedPlaceholderBuilder(scriptRootPath, _loggerProvider, "HttpRequestDataFunction");

            // remove WEBSITE_USE_PLACEHOLDER_DOTNETISOLATED
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteUsePlaceholderDotNetIsolated, null);

            await using var stoppable = new StoppableHost(builder.Build());

            var host = stoppable.Inner;

            await host.StartAsync();

            var client = host.GetTestClient();
            var response = await client.GetAsync("api/warmup");
            response.EnsureSuccessStatusCode();

            // Validate that the channel is set up with native worker
            var webChannelManager = host.Services.GetService<IWebHostRpcWorkerChannelManager>();

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

        private IHostBuilder InitializeDotNetIsolatedPlaceholderBuilder(string scriptRootPath, TestLoggerProvider testLoggerProvider, params string[] functions)
        {
            return InitializeDotNetIsolatedPlaceholderBuilder(scriptRootPath, testLoggerProvider, null, functions);
        }

        private IHostBuilder InitializeDotNetIsolatedPlaceholderBuilder(string scriptRootPath, TestLoggerProvider testLoggerProvider, Dictionary<string, string> environmentVariables, params string[] functions)
        {
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, "dotnet-isolated");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteUsePlaceholderDotNetIsolated, "1");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, ScriptConstants.FeatureFlagEnableWorkerIndexing);
            _environment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeVersionSettingName, "6.0");

            if (environmentVariables is not null)
            {
                foreach (var (key, value) in environmentVariables)
                {
                    _environment.SetEnvironmentVariable(key, value);
                }
            }

            var builder = CreateStandbyHostBuilder(testLoggerProvider, functions);

            builder.ConfigureAppConfiguration(config =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string>
                {
                    { _scriptRootConfigPath, scriptRootPath },
                });
            });

            return builder;
        }

        private IHostBuilder CreateStandbyHostBuilder(TestLoggerProvider loggerProvider, params string[] functions)
        {
            loggerProvider = loggerProvider ?? _loggerProvider;
            var builder = Program.CreateHostBuilder()
                .ConfigureWebHost(webHostBuilder =>
                {
                    webHostBuilder.UseTestServer();
                })
                .ConfigureAppConfiguration(c =>
                {
                    var inMemorySettings = new Dictionary<string, string>
                    {
                        { _scriptRootConfigPath, _specializedScriptRoot },
                        { _logPathConfigPath, _testLogPath }
                    };

                    string workerRuntime = _environment.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime);
                    if (workerRuntime is not null)
                    {
                        inMemorySettings[EnvironmentSettingNames.FunctionWorkerRuntime] = workerRuntime;
                    }

                    c.AddInMemoryCollection(inMemorySettings);
                })
                .ConfigureLogging(b =>
                {
                    b.AddProvider(loggerProvider);
                    b.AddFilter<TestLoggerProvider>("Microsoft.Azure.WebJobs", LogLevel.Debug);
                    b.AddFilter<TestLoggerProvider>("Worker", LogLevel.Debug);
                    b.AddFilter<TestLoggerProvider>("Host.LanguageWorkerConfig", LogLevel.Trace);
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
                        logging.AddProvider(loggerProvider);
                    });

                    s.PostConfigure<ScriptJobHostOptions>(o =>
                    {
                        // Only load the function we care about, but not during standby
                        if (o.RootScriptPath != _standbyPath)
                        {
                            o.Functions = functions;
                        }
                    });

                    _customizeScriptHostServices?.Invoke(s);
                });

            return builder;
        }

        private void AssertLanguageWorkerOptionsSetupCount(int expected)
        {
            // Verify LanguageWorkerOptions setup count
            var workerConfigLogs = _loggerProvider.GetAllLogMessages()
                .Where(p => p.FormattedMessage is not null && p.FormattedMessage.Contains("Workers Directory set to:"))
                .ToArray();
            Assert.Equal(expected, workerConfigLogs.Length);
        }

        private void AssertWorkerProcessStartupCount(int expected)
        {
            var workerStartLogs = _loggerProvider.GetAllLogMessages()
                .Where(p => p.FormattedMessage is not null && p.FormattedMessage.Contains("Starting worker process with FileName:"))
                .ToArray();
            Assert.Equal(expected, workerStartLogs.Length);
        }

        private class InfiniteTimerStandbyManager : StandbyManager
        {
            public InfiniteTimerStandbyManager(IScriptHostManager scriptHostManager, IWebHostWorkerManager rpcWorkerChannelManager,
                IConfiguration configuration, IScriptWebHostEnvironment webHostEnvironment, IEnvironment environment,
                IOptionsMonitor<ScriptApplicationHostOptions> options, ILogger<StandbyManager> logger, HostNameProvider hostNameProvider, IHostApplicationLifetime applicationLifetime)
                : base(scriptHostManager, rpcWorkerChannelManager, configuration, webHostEnvironment, environment, options,
                      logger, hostNameProvider, applicationLifetime, TimeSpan.FromMilliseconds(-1), new TestMetricsLogger())
            {
            }
        }

        private class PausingScriptHostBuilder : IScriptHostBuilder
        {
            private readonly DefaultScriptHostBuilder _inner;
            private readonly IOptionsMonitor<ScriptApplicationHostOptions> _options;

            public PausingScriptHostBuilder(IOptionsMonitor<ScriptApplicationHostOptions> options, IServiceProvider rootServiceProvider, IServiceCollection rootServices)
            {
                _inner = new DefaultScriptHostBuilder(options, rootServices, rootServiceProvider);
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

        /// <summary>
        /// Wraps an <see cref="IHost"/> to ensure <see cref="IHost.StopAsync"/> is called
        /// before disposal, guaranteeing <see cref="FileMonitoringService.StopAsync"/> runs
        /// and file watchers are unsubscribed even if a test assertion fails.
        /// </summary>
        private sealed class StoppableHost : IAsyncDisposable
        {
            public IHost Inner { get; }

            public StoppableHost(IHost host) => Inner = host;

            public IServiceProvider Services => Inner.Services;

            public async ValueTask DisposeAsync()
            {
                try
                {
                    await Inner.StopAsync();
                }
                catch
                {
                }

                Inner.Dispose();
            }
        }
    }
}
