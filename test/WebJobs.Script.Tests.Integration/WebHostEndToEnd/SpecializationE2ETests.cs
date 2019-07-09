// Copyright (c) .NET Foundation. All rights reserved.
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
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Logging.ApplicationInsights;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class SpecializationE2ETests
    {
        private static SemaphoreSlim _pause = new SemaphoreSlim(1, 1);

        [Fact]
        public async Task ApplicationInsights_InvocationsContainDifferentOperationIds()
        {
            // Verify that when a request specializes the host we don't capture the context
            // of that request. Application Insights uses this context to correlate telemetry
            // so it had a confusing effect. Previously all TimerTrigger traces would have the
            // operation id of this request and all host logs would as well.

            // Start a host in standby mode.
            StandbyManager.ResetChangeToken();

            string standbyPath = Path.Combine(Path.GetTempPath(), "functions", "standby", "wwwroot");
            string specializedScriptRoot = @"TestScripts\CSharp";
            string scriptRootConfigPath = ConfigurationPath.Combine(ConfigurationSectionNames.WebHost, nameof(ScriptApplicationHostOptions.ScriptPath));

            var settings = new Dictionary<string, string>()
            {
                { EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1" },
                { EnvironmentSettingNames.AzureWebsiteContainerReady, null },
             };

            var environment = new TestEnvironment(settings);
            var loggerProvider = new TestLoggerProvider();
            var channel = new TestTelemetryChannel();

            var builder = Program.CreateWebHostBuilder()
                .ConfigureLogging(b =>
                {
                    b.AddProvider(loggerProvider);
                })
                .ConfigureAppConfiguration(c =>
                {
                    c.AddInMemoryCollection(new Dictionary<string, string>
                    {
                        { scriptRootConfigPath, specializedScriptRoot }
                    });
                })
                .ConfigureServices((bc, s) =>
                {
                    s.AddSingleton<IEnvironment>(environment);

                    // Ensure that we don't have a race between the timer and the 
                    // request for triggering specialization.
                    s.AddSingleton<IStandbyManager, InfiniteTimerStandbyManager>();
                })
                .AddScriptHostBuilder(webJobsBuilder =>
                {
                    webJobsBuilder.Services.AddSingleton<ITelemetryChannel>(_ => channel);

                    webJobsBuilder.Services.Configure<FunctionResultAggregatorOptions>(o =>
                    {
                        o.IsEnabled = false;
                    });

                    webJobsBuilder.Services.PostConfigure<ApplicationInsightsLoggerOptions>(o =>
                    {
                        o.SamplingSettings = null;
                    });

                    webJobsBuilder.Services.PostConfigure<ScriptJobHostOptions>(o =>
                    {
                        // Only load the function we care about, but not during standby
                        if (o.RootScriptPath != standbyPath)
                        {
                            o.Functions = new[]
                            {
                                "OneSecondTimer",
                                "FunctionExecutionContext"
                            };
                        }
                    });
                })
                .ConfigureScriptHostAppConfiguration(c =>
                {
                    c.AddInMemoryCollection(new Dictionary<string, string>
                    {
                        [EnvironmentSettingNames.AppInsightsInstrumentationKey] = "some_key"
                    });
                });

            using (var testServer = new TestServer(builder))
            {
                var client = testServer.CreateClient();

                HttpResponseMessage response = await client.GetAsync("api/warmup");
                Assert.True(response.IsSuccessStatusCode, loggerProvider.GetLog());

                // Now that standby mode is warmed up, set the specialization properties...
                environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteContainerReady, "1");
                environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");

                // ...and issue a request which will force specialization.
                response = await client.GetAsync("api/functionexecutioncontext");
                Assert.True(response.IsSuccessStatusCode, loggerProvider.GetLog());

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
            // Start a host in standby mode.
            StandbyManager.ResetChangeToken();

            string standbyPath = Path.Combine(Path.GetTempPath(), "functions", "standby", "wwwroot");
            string specializedScriptRoot = @"TestScripts\CSharp";
            string scriptRootConfigPath = ConfigurationPath.Combine(ConfigurationSectionNames.WebHost, nameof(ScriptApplicationHostOptions.ScriptPath));

            var settings = new Dictionary<string, string>()
            {
                { EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1" },
                { EnvironmentSettingNames.AzureWebsiteContainerReady, null },
             };

            var environment = new TestEnvironment(settings);
            var loggerProvider = new TestLoggerProvider();

            var builder = Program.CreateWebHostBuilder()
                .ConfigureLogging(b =>
                {
                    b.AddProvider(loggerProvider);
                })
                .ConfigureAppConfiguration(c =>
                {
                    c.AddInMemoryCollection(new Dictionary<string, string>
                    {
                        { scriptRootConfigPath, specializedScriptRoot }
                    });
                })
                .ConfigureServices((bc, s) =>
                {
                    s.AddSingleton<IEnvironment>(environment);

                    // Ensure that we don't have a race between the timer and the 
                    // request for triggering specialization.
                    s.AddSingleton<IStandbyManager, InfiniteTimerStandbyManager>();

                    s.AddSingleton<IScriptHostBuilder, PausingScriptHostBuilder>();
                })
                .AddScriptHostBuilder(webJobsBuilder =>
                {
                    webJobsBuilder.Services.PostConfigure<ScriptJobHostOptions>(o =>
                    {
                        // Only load the function we care about, but not during standby
                        if (o.RootScriptPath != standbyPath)
                        {
                            o.Functions = new[]
                            {
                                "FunctionExecutionContext"
                            };
                        }
                    });
                });

            using (var testServer = new TestServer(builder))
            {
                var client = testServer.CreateClient();

                var response = await client.GetAsync("api/warmup");
                response.EnsureSuccessStatusCode();

                await _pause.WaitAsync();

                List<Task<HttpResponseMessage>> requestTasks = new List<Task<HttpResponseMessage>>();

                environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteContainerReady, "1");
                environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");

                for (int i = 0; i < 100; i++)
                {
                    requestTasks.Add(client.GetAsync("api/functionexecutioncontext"));
                }

                ThreadPool.GetAvailableThreads(out int originalWorkerThreads, out int originalcompletionThreads);
                Thread.Sleep(5000);
                ThreadPool.GetAvailableThreads(out int workerThreads, out int completionThreads);

                _pause.Release();

                Assert.True(workerThreads >= originalWorkerThreads, $"Available ThreadPool threads should not have decreased. Actual: {workerThreads}. Original: {originalWorkerThreads}.");

                await Task.WhenAll(requestTasks);

                Assert.True(requestTasks.All(t => t.Result.StatusCode == HttpStatusCode.OK));
            }
        }

        private class InfiniteTimerStandbyManager : StandbyManager
        {
            public InfiniteTimerStandbyManager(IScriptHostManager scriptHostManager, IWebHostLanguageWorkerChannelManager languageWorkerChannelManager,
                IConfiguration configuration, IScriptWebHostEnvironment webHostEnvironment, IEnvironment environment,
                IOptionsMonitor<ScriptApplicationHostOptions> options, ILogger<StandbyManager> logger, HostNameProvider hostNameProvider)
                : base(scriptHostManager, languageWorkerChannelManager, configuration, webHostEnvironment, environment, options,
                      logger, hostNameProvider, TimeSpan.FromMilliseconds(-1))
            {
            }
        }

        private class PausingScriptHostBuilder : IScriptHostBuilder
        {
            private readonly DefaultScriptHostBuilder _inner;

            public PausingScriptHostBuilder(IOptionsMonitor<ScriptApplicationHostOptions> options, IServiceProvider root, IServiceScopeFactory scope)
            {
                _inner = new DefaultScriptHostBuilder(options, root, scope);
            }

            public IHost BuildHost(bool skipHostStartup, bool skipHostConfigurationParsing)
            {
                _pause.WaitAsync().GetAwaiter().GetResult();

                IHost host = _inner.BuildHost(skipHostStartup, skipHostConfigurationParsing);

                _pause.Release();

                return host;
            }
        }
    }
}
