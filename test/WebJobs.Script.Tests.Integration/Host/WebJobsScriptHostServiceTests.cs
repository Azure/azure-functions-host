// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license Informationrmation.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Description;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Script.Scale;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;
using IApplicationLifetime = Microsoft.AspNetCore.Hosting.IApplicationLifetime;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.Host
{
    public class WebJobsScriptHostServiceTests : IDisposable
    {
        private readonly string TestScriptPath = @"TestScripts\CSharp";
        private readonly string TestLogPath = Path.Combine(TestHelpers.FunctionsTestDirectory, "Logs", Guid.NewGuid().ToString(), @"Functions");

        private readonly WebJobsScriptHostService _scriptHostService;
        private readonly Mock<IApplicationLifetime> _mockApplicationLifetime;
        private readonly Mock<IEnvironment> _mockEnvironment;
        private readonly TestFunctionHost _testHost;
        private readonly Collection<string> _exceededCounters = new Collection<string>();
        private readonly HostHealthMonitorOptions _healthMonitorOptions;
        private bool _countersExceeded;
        private bool _shutdownCalled;

        public WebJobsScriptHostServiceTests()
        {
            // configure the monitor so it will fail within a couple seconds
            _healthMonitorOptions = new HostHealthMonitorOptions
            {
                HealthCheckInterval = TimeSpan.FromMilliseconds(100),
                HealthCheckWindow = TimeSpan.FromSeconds(1),
                HealthCheckThreshold = 5
            };
            var wrappedHealthMonitorOptions = new OptionsWrapper<HostHealthMonitorOptions>(_healthMonitorOptions);

            _mockApplicationLifetime = new Mock<IApplicationLifetime>(MockBehavior.Loose);
            _mockApplicationLifetime.Setup(p => p.StopApplication())
               .Callback(() =>
               {
                   _shutdownCalled = true;
               });

            _mockEnvironment = new Mock<IEnvironment>();
            var mockServiceProvider = new Mock<IServiceProvider>(MockBehavior.Strict);
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteInstanceId)).Returns("testapp");
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHostName)).Returns("testapp");

            var mockHostPerformanceManager = new Mock<HostPerformanceManager>(_mockEnvironment.Object, wrappedHealthMonitorOptions, mockServiceProvider.Object, null);

            mockHostPerformanceManager.Setup(p => p.PerformanceCountersExceeded(It.IsAny<Collection<string>>(), It.IsAny<ILogger>()))
                .Callback<Collection<string>, ILogger>((c, l) =>
                {
                    if (_countersExceeded)
                    {
                        foreach (var counter in _exceededCounters)
                        {
                            c.Add(counter);
                        }
                    }
                })
                .Returns(() => _countersExceeded);

            _testHost = new TestFunctionHost(TestScriptPath, TestLogPath,
                configureWebHostServices: services =>
                {
                    services.AddSingleton<IOptions<HostHealthMonitorOptions>>(wrappedHealthMonitorOptions);
                    services.AddSingleton<IApplicationLifetime>(_mockApplicationLifetime.Object);
                    services.AddSingleton<IEnvironment>(_mockEnvironment.Object);
                    services.AddSingleton<HostPerformanceManager>(mockHostPerformanceManager.Object);

                    services.AddSingleton<IConfigureBuilder<IWebJobsBuilder>>(new DelegatedConfigureBuilder<IWebJobsBuilder>(b =>
                    {
                        b.UseHostId("1234");
                        b.Services.Configure<ScriptJobHostOptions>(o => o.Functions = new[] { "ManualTrigger", "Scenarios" });
                    }));
                },
                configureScriptHostWebJobsBuilder: builder =>
                {
                    builder.AddExtension<TestWebHookExtension>();
                });

            _scriptHostService = _testHost.JobHostServices.GetService<IScriptHostManager>() as WebJobsScriptHostService;
        }

        [Fact]
        public async Task GetHostKeys_DelaysUntilHostInitialized()
        {
            TestWebHookExtension.Initializing = false;
            TestWebHookExtension.Delay = 2000;

            try
            {
                // reset secrets to ensure the extension secret is not yet added
                var testSecretManager = (TestSecretManager)_testHost.JobHostServices.GetService<ISecretManagerProvider>().Current;
                testSecretManager.Reset();

                // initiate a restart and a keys request concurrently
                var keysRequest = new HttpRequestMessage(HttpMethod.Get, "http://localhost/admin/host/systemkeys");
                keysRequest.Headers.Add(AuthenticationLevelHandler.FunctionsKeyHeaderName, TestSecretManager.TestMasterKey);
                JObject keysContent = null;
                var keysTask = Task.Run(async () =>
                {
                    // wait until the test extension has begun initialization - this way we know
                    // host initialization is in progress
                    await TestHelpers.Await(() =>
                    {
                        return !TestWebHookExtension.Initializing;
                    });

                    // make the keys request while during initialization BEFORE the extension
                    // has had a chance to create the extension system key
                    var response = await _testHost.HttpClient.SendAsync(keysRequest);
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                    keysContent = await response.Content.ReadAsAsync<JObject>();
                });

                var restartTask = _testHost.RestartAsync(CancellationToken.None);

                await Task.WhenAll(restartTask, keysTask);

                // verify the extension system key is present
                JArray keys = (JArray)keysContent["keys"];
                var extensionKey = (JObject)keys.Cast<JObject>().SingleOrDefault(p => (string)p["name"] == "testwebhook_extension");
                Assert.NotNull(extensionKey);
                string extensionKeyValue = (string)extensionKey["value"];
                Assert.False(string.IsNullOrEmpty(extensionKeyValue));
            }
            finally
            {
                TestWebHookExtension.Initializing = false;
                TestWebHookExtension.Delay = 0;
            }
        }

        [Fact]
        public void InitializationLogs_AreEmitted()
        {
            // verify startup trace logs
            string[] expectedPatternsWebHost = new string[]
            {
                "Information Reading host configuration file",
                "Information Host configuration file read",
            };

            string[] expectedPatternsScriptHost = new string[]
            {
                @"Information Generating 2 job function\(s\)",
                "Host initialization: ConsecutiveErrors=0, StartupCount=1",
                @"Information Starting Host \(HostId=(.*), InstanceId=(.*), Version=(.+), ProcessId=[0-9]+, AppDomainId=[0-9]+, InDebugMode=False, InDiagnosticMode=False, FunctionsExtensionVersion=\(null\)\)",
                "Information Found the following functions:",
                "Information Job host started",
            };

            IList<LogMessage> logs = _testHost.GetWebHostLogMessages();
            foreach (string pattern in expectedPatternsWebHost)
            {
                Assert.True(logs.Any(p => Regex.IsMatch($"{p.Level} {p.FormattedMessage}", pattern)), $"Expected trace event {pattern} not found.");
            }

            logs = _testHost.GetScriptHostLogMessages();
            foreach (string pattern in expectedPatternsScriptHost)
            {
                Assert.True(logs.Any(p => Regex.IsMatch($"{p.Level} {p.FormattedMessage}", pattern)), $"Expected trace event {pattern} not found.{Environment.NewLine}{_testHost.GetLog()}");
            }
        }

        [Fact]
        public void WebHookProvider_IsRegistered()
        {
            Assert.NotNull(_scriptHostService.Services.GetService<IWebHookProvider>());
            Assert.NotNull(_scriptHostService.Services.GetService<IScriptWebHookProvider>());
        }

        [Fact]
        public async Task HostHealthMonitor_TriggersShutdown_WhenHostUnhealthy()
        {
            Assert.Equal(ScriptHostState.Running, _scriptHostService.State);

            // make host unhealthy
            _exceededCounters.Add("Connections");
            _countersExceeded = true;

            await TestHelpers.Await(() => _shutdownCalled);

            Assert.Equal(ScriptHostState.Error, _scriptHostService.State);
            _mockApplicationLifetime.Verify(p => p.StopApplication(), Times.Once);

            // we expect a few restart iterations
            var scriptHostLogMessages = _testHost.GetScriptHostLogMessages();
            var webHostLogMessages = _testHost.GetWebHostLogMessages();

            var thresholdErrors = scriptHostLogMessages.Where(p => p.Exception is InvalidOperationException && p.Exception.Message == "Host thresholds exceeded: [Connections]. For more information, see https://aka.ms/functions-thresholds.");
            var count = thresholdErrors.Count();
            Assert.True(count > 0);

            var log = webHostLogMessages.First(p => p.FormattedMessage == "Host is unhealthy. Initiating a restart." && p.Level == LogLevel.Error);
            Assert.Equal(LogLevel.Error, log.Level);

            log = webHostLogMessages.First(p => p.FormattedMessage == "Host unhealthy count exceeds the threshold of 5 for time window 00:00:01. Initiating shutdown.");
            Assert.Equal(LogLevel.Error, log.Level);

            Assert.Contains(scriptHostLogMessages, p => p.FormattedMessage == "Stopping JobHost");
        }

        [Fact]
        public async Task HostHealthMonitor_RestartsSuccessfully_WhenHostRecovers()
        {
            Assert.Equal(ScriptHostState.Running, _scriptHostService.State);

            // crank this number up so we don't do a shutdown but instead
            // continue the retry loop
            _healthMonitorOptions.HealthCheckThreshold = 100;

            // now that host is running make host unhealthy and wait
            // for host shutdown
            _exceededCounters.Add("Connections");
            _countersExceeded = true;

            await TestHelpers.Await(() => _scriptHostService.State == ScriptHostState.Error);
            var lastError = _scriptHostService.LastError;
            Assert.Equal("Host thresholds exceeded: [Connections]. For more information, see https://aka.ms/functions-thresholds.", _scriptHostService.LastError.Message);

            // verify we are in a startup/retry loop
            await TestHelpers.Await(() =>
            {
                var allLogs = _testHost.GetLog();
                return allLogs.Contains("Host initialization: ConsecutiveErrors=3");
            });
            Assert.Equal(ScriptHostState.Error, _scriptHostService.State);
            _mockApplicationLifetime.Verify(p => p.StopApplication(), Times.Never);

            // after a few retries, put the host back to health and verify
            // it starts successfully
            _exceededCounters.Clear();
            _countersExceeded = false;

            await TestHelpers.Await(() => _scriptHostService.State == ScriptHostState.Running);
            Assert.Null(_scriptHostService.LastError);

            var logMessages = _testHost.GetScriptHostLogMessages();
            Assert.Contains(logMessages, p => p.FormattedMessage == "Job host started");
        }

        [Fact]
        public void IsHostHealthy_ReturnsExpectedResult()
        {
            _healthMonitorOptions.Enabled = false;
            Assert.True(_scriptHostService.IsHostHealthy());

            _healthMonitorOptions.Enabled = true;
            Assert.True(_scriptHostService.IsHostHealthy());

            _countersExceeded = true;
            _exceededCounters.Add("Foo");
            _exceededCounters.Add("Bar");
            Assert.False(_scriptHostService.IsHostHealthy());

            var ex = Assert.Throws<InvalidOperationException>(() => _scriptHostService.IsHostHealthy(true));
            Assert.Equal("Host thresholds exceeded: [Foo, Bar]. For more information, see https://aka.ms/functions-thresholds.", ex.Message);

            _healthMonitorOptions.Enabled = false;
            Assert.True(_scriptHostService.IsHostHealthy());
        }

        [Fact]
        public async Task Restarts_CanCancel_Restarts()
        {
            // We need a custom setup.
            _testHost.Dispose();
            int buildCalls = 0;

            var testHost = new TestFunctionHost(TestScriptPath, TestLogPath,
              configureWebHostServices: services =>
              {
                  services.Configure<LoggerFilterOptions>(o =>
                  {
                      o.MinLevel = LogLevel.Debug;
                  });

                  services.AddSingleton<IEnvironment>(_mockEnvironment.Object);

                  services.AddSingleton<IConfigureBuilder<IWebJobsBuilder>>(new DelegatedConfigureBuilder<IWebJobsBuilder>(b =>
                  {
                      b.Services.Configure<ScriptJobHostOptions>(o => o.Functions = new string[0]);

                      b.Services.Configure<LoggerFilterOptions>(o =>
                      {
                          o.MinLevel = LogLevel.Debug;
                      });
                  }));

                  services.AddSingleton<IScriptHostBuilder>(s =>
                  {
                      var appHostOptions = s.GetService<IOptionsMonitor<ScriptApplicationHostOptions>>();
                      var rootServiceScopeFactory = s.GetService<IServiceScopeFactory>();

                      IHost Intercept(IScriptHostBuilder builder, bool skipHostStartup, bool skipHostConfigurationParsing)
                      {
                          // We want the the repro to be:
                          // 1. Succeeds, running.
                          // 2. Restart, this host fails while starting.
                          // 3. Restart, this host succeeds.
                          // 4+. All fail, meaning the restart from #2 never succeeds.
                          buildCalls++;
                          if (buildCalls == 2 || buildCalls >= 4)
                          {
                              var mockHost = new Mock<IHost>();
                              mockHost
                                .Setup(p => p.StartAsync(It.IsAny<CancellationToken>()))
                                .Throws(new HostInitializationException());

                              return mockHost.Object;
                          }

                          return builder.BuildHost(skipHostStartup, skipHostConfigurationParsing);
                      }

                      return new InterceptingScriptHostBuilder(appHostOptions, s, rootServiceScopeFactory, Intercept);
                  });
              });

            var scriptHostService = testHost.WebHostServices.GetService<IScriptHostManager>() as WebJobsScriptHostService;

            List<Task> restarts = new List<Task>();
            restarts.Add(testHost.RestartAsync(CancellationToken.None));
            restarts.Add(testHost.RestartAsync(CancellationToken.None));

            await Task.WhenAll(restarts);

            await TestHelpers.Await(() => scriptHostService.State == ScriptHostState.Running, userMessageCallback: testHost.GetLog);
        }

        public void Dispose()
        {
            _testHost?.Dispose();
        }

        private class InterceptingScriptHostBuilder : IScriptHostBuilder
        {
            private readonly DefaultScriptHostBuilder _builder;
            private readonly Func<IScriptHostBuilder, bool, bool, IHost> _interceptCallback;

            public InterceptingScriptHostBuilder(IOptionsMonitor<ScriptApplicationHostOptions> appHostOptions, IServiceProvider rootServiceProvider,
                IServiceScopeFactory rootServiceScopeFactory, Func<IScriptHostBuilder, bool, bool, IHost> interceptCallback)
            {
                _builder = new DefaultScriptHostBuilder(appHostOptions, rootServiceProvider, rootServiceScopeFactory);
                _interceptCallback = interceptCallback;
            }

            public IHost BuildHost(bool skipHostStartup, bool skipHostConfigurationParsing)
            {
                return _interceptCallback(_builder, skipHostStartup, skipHostConfigurationParsing);
            }
        }

        [Extension("TestWebHook", "TestWebHook")]
        private class TestWebHookExtension : IExtensionConfigProvider, IAsyncConverter<HttpRequestMessage, HttpResponseMessage>
        {
            public static bool Initializing;
            public static int Delay = 0;

            public Task<HttpResponseMessage> ConvertAsync(HttpRequestMessage input, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public void Initialize(ExtensionConfigContext context)
            {
                Initializing = true;

                Task.Delay(Delay).GetAwaiter().GetResult();

                Uri url = context.GetWebhookHandler();
            }
        }
    }
}
