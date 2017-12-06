// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Routing;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Moq;
using Newtonsoft.Json.Linq;
using WebJobs.Script.Tests;
using Xunit;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using System.Net;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class WebScriptHostManagerTests : IClassFixture<WebScriptHostManagerTests.Fixture>, IDisposable
    {
        private readonly ScriptSettingsManager _settingsManager;
        private readonly TempDirectory _secretsDirectory = new TempDirectory();
        private readonly WebScriptHostManager _hostManager;
        private readonly Mock<IScriptHostFactory> _mockScriptHostFactory;
        private ScriptHostConfiguration _config;
        private WebScriptHostManagerTests.Fixture _fixture;

        public WebScriptHostManagerTests(WebScriptHostManagerTests.Fixture fixture)
        {
            _fixture = fixture;
            _settingsManager = ScriptSettingsManager.Instance;

            string functionTestDir = Path.Combine(_fixture.TestFunctionRoot, Guid.NewGuid().ToString());
            Directory.CreateDirectory(functionTestDir);
            string logDir = Path.Combine(_fixture.TestLogsRoot, Guid.NewGuid().ToString());
            string secretsDir = Path.Combine(_fixture.TestSecretsRoot, Guid.NewGuid().ToString());

            _config = new ScriptHostConfiguration
            {
                RootLogPath = logDir,
                RootScriptPath = functionTestDir,
                FileLoggingMode = FileLoggingMode.Always,
            };

            var secretsRepository = new FileSystemSecretsRepository(_secretsDirectory.Path);
            SecretManager secretManager = new SecretManager(_settingsManager, secretsRepository, null);
            WebHostSettings webHostSettings = new WebHostSettings
            {
                SecretsPath = _secretsDirectory.Path
            };

            var mockEventManager = new Mock<IScriptEventManager>();
            _mockScriptHostFactory = new Mock<IScriptHostFactory>();
            IWebJobsRouter router = fixture.CreateRouter();

            _hostManager = new WebScriptHostManager(_config, new TestSecretManagerFactory(secretManager), mockEventManager.Object,
                _settingsManager, webHostSettings, router, secretsRepositoryFactory: new DefaultSecretsRepositoryFactory(),
                hostTimeoutSeconds: 2,  hostPollingIntervalMilliseconds: 500, scriptHostFactory: _mockScriptHostFactory.Object);
        }

        [Fact(Skip = "Investigate test failure (tracked by https://github.com/Azure/azure-webjobs-sdk-script/issues/2022)")]
        public async Task FunctionInvoke_SystemTraceEventsAreEmitted()
        {
            await _fixture.InitializationTask;
            _fixture.EventGenerator.Events.Clear();

            var host = _fixture.HostManager.Instance;
            var input = Guid.NewGuid().ToString();
            var parameters = new Dictionary<string, object>
            {
                { "input", input }
            };
            await host.CallAsync("ManualTrigger", parameters);

            // it's possible that the TimerTrigger fires during this so filter them out.

            string[] events = _fixture.EventGenerator.Events.Where(e => !e.Contains("TimerTrigger")).ToArray();
            Assert.True(events.Length == 4, $"Expected 4 events. Actual: {events.Length}. Actual events: {Environment.NewLine}{string.Join(Environment.NewLine, events)}");
            Assert.StartsWith("Info ManualTrigger Function started (Id=", events[0]); // From fast-logger pre-bind notification, FunctionLogEntry.IsStart
            Assert.StartsWith("Info WebJobs.Execution Executing 'Functions.ManualTrigger' (Reason='This function was programmatically called via the host APIs.', Id=", events[1]); // from TraceWriterFunctionInstanceLogger
            Assert.StartsWith("Info ManualTrigger Function completed (Success, Id=", events[2]); // From fast-logger, FunctionLogEntry.IsComplete
            Assert.StartsWith("Info WebJobs.Execution Executed 'Functions.ManualTrigger' (Succeeded, Id=", events[3]);

            // make sure the user log wasn't traced
            Assert.False(_fixture.EventGenerator.Events.Any(p => p.Contains("ManualTrigger function invoked!")));
        }

        [Fact(Skip = "Investigate test failure (tracked by https://github.com/Azure/azure-webjobs-sdk-script/issues/2022)")]
        public async Task FunctionLogFilesArePurgedOnStartup()
        {
            await _fixture.InitializationTask;
            var logDirs = Directory.EnumerateDirectories(_fixture.FunctionsLogDir).Select(p => Path.GetFileName(p).ToLowerInvariant()).ToArray();

            // Even if a function is invalid an not part of the active
            // loaded functions, we don't want to purge data for it
            Assert.True(logDirs.Contains("invalid"));

            Assert.False(logDirs.Contains("foo"));
            Assert.False(logDirs.Contains("bar"));
            Assert.False(logDirs.Contains("baz"));
        }

        [Fact(Skip = "Investigate test failure (tracked by https://github.com/Azure/azure-webjobs-sdk-script/issues/2022)")]
        public async Task SecretFilesArePurgedOnStartup()
        {
            await _fixture.InitializationTask;
            var secretFiles = Directory.EnumerateFiles(_fixture.SecretsPath).Select(p => Path.GetFileName(p)).OrderBy(p => p).ToArray();
            Assert.Equal(4, secretFiles.Length);

            Assert.Equal(ScriptConstants.HostMetadataFileName, secretFiles[0]);
            Assert.Equal("Invalid.json", secretFiles[1]);
            Assert.Equal("QueueTriggerToBlob.json", secretFiles[2]);
            Assert.Equal("WebHookTrigger.json", secretFiles[3]);
        }

        [Fact(Skip = "Investigate test failure (tracked by https://github.com/Azure/azure-webjobs-sdk-script/issues/2022)")]
        public async Task EmptyHost_StartsSuccessfully()
        {
            await _fixture.InitializationTask;

            string functionTestDir = Path.Combine(_fixture.TestFunctionRoot, Guid.NewGuid().ToString());
            Directory.CreateDirectory(functionTestDir);

            // important for the repro that these directories no not exist
            string logDir = Path.Combine(_fixture.TestLogsRoot, Guid.NewGuid().ToString());
            string secretsDir = Path.Combine(_fixture.TestSecretsRoot, Guid.NewGuid().ToString());

            JObject hostConfig = new JObject
            {
                { "id", "123456" }
            };
            File.WriteAllText(Path.Combine(functionTestDir, ScriptConstants.HostMetadataFileName), hostConfig.ToString());

            ScriptHostConfiguration config = new ScriptHostConfiguration
            {
                RootScriptPath = functionTestDir,
                RootLogPath = logDir,
                FileLoggingMode = FileLoggingMode.Always
            };
            string connectionString = AmbientConnectionStringProvider.Instance.GetConnectionString(ConnectionStringNames.Storage);
            ISecretsRepository repository = new BlobStorageSecretsRepository(secretsDir, connectionString, "EmptyHost_StartsSuccessfully");
            ISecretManager secretManager = new SecretManager(_settingsManager, repository, null);
            WebHostSettings webHostSettings = new WebHostSettings();
            webHostSettings.SecretsPath = _secretsDirectory.Path;
            var mockEventManager = new Mock<IScriptEventManager>();
            IWebJobsRouter router = _fixture.CreateRouter();
            ScriptHostManager hostManager = new WebScriptHostManager(config, new TestSecretManagerFactory(secretManager), mockEventManager.Object, _settingsManager, webHostSettings, router);

            Task runTask = Task.Run(() => hostManager.RunAndBlock());

            await TestHelpers.Await(() => hostManager.State == ScriptHostState.Running, timeout: 10000);

            hostManager.Stop();
            Assert.Equal(ScriptHostState.Default, hostManager.State);

            // give some time for the logs to be flushed fullly
            await Task.Delay(FileTraceWriter.LogFlushIntervalMs * 3);

            string hostLogFilePath = Directory.EnumerateFiles(Path.Combine(logDir, "Host")).Single();
            string hostLogs = File.ReadAllText(hostLogFilePath);

            Assert.Contains("Generating 0 job function(s)", hostLogs);
            Assert.Contains("No job functions found.", hostLogs);
            Assert.Contains("Job host started", hostLogs);
            Assert.Contains("Job host stopped", hostLogs);
        }

        [Fact]
        public async Task MultipleHostRestarts()
        {
            int count = 0;
            _mockScriptHostFactory
                .Setup(p => p.Create(It.IsAny<IScriptHostEnvironment>(), It.IsAny<IScriptEventManager>(), _settingsManager, _config, It.IsAny<ILoggerFactoryBuilder>()))
                .Callback(() =>
                {
                    count++;
                })
                .Throws(new Exception("Kaboom!"));

            Task runTask = Task.Run(() => _hostManager.RunAndBlock());

            await TestHelpers.Await(() =>
            {
                return count > 3;
            });

            _hostManager.Stop();
            Assert.Equal(ScriptHostState.Default, _hostManager.State);

            // regression test: previously on multiple restarts we were recomposing
            // the writer on each restart, resulting in a nested chain of writers
            // increasing on each restart
            Assert.Equal(typeof(SystemTraceWriter), _config.TraceWriter.GetType());
        }

        [Fact]
        public async Task DelayUntilHostReady_HostInErrorState_ThrowsImmediately()
        {
            var settingsManager = ScriptSettingsManager.Instance;
            var eventManager = new Mock<IScriptEventManager>();
            var managerMock = new Mock<WebScriptHostManager>(MockBehavior.Strict, new ScriptHostConfiguration(), new TestSecretManagerFactory(),
                eventManager.Object, settingsManager, new WebHostSettings { SecretsPath = _secretsDirectory.Path }, null, null, null, null, null, 1, 50);

            managerMock.SetupGet(p => p.State).Returns(ScriptHostState.Error);
            managerMock.SetupGet(p => p.LastError).Returns(new Exception());

            var ex = await Assert.ThrowsAsync<HttpException>(async () => await WebScriptHostManager.DelayUntilHostReady(managerMock.Object, 1, 50));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (HttpStatusCode)ex.StatusCode);
            managerMock.VerifyGet(p => p.State, Times.Exactly(5));
        }

        [Fact]
        public async Task DelayUntilHostReady_HostNotRunning_Returns503()
        {
            var settingsManager = ScriptSettingsManager.Instance;
            var eventManager = new Mock<IScriptEventManager>();
            var managerMock = new Mock<WebScriptHostManager>(MockBehavior.Strict, new ScriptHostConfiguration(), 
                new TestSecretManagerFactory(), eventManager.Object, settingsManager, new WebHostSettings { SecretsPath = _secretsDirectory.Path }, 
                null, null, null, null, null, 1, 50);

            managerMock.SetupGet(p => p.State).Returns(ScriptHostState.Default);
            managerMock.SetupGet(p => p.LastError).Returns((Exception)null);

            var ex = await Assert.ThrowsAsync<HttpException>(async () => await WebScriptHostManager.DelayUntilHostReady(managerMock.Object, 1, 50));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (HttpStatusCode)ex.StatusCode);
        }

        public void Dispose()
        {
            _secretsDirectory.Dispose();
        }

        public class Fixture : IDisposable
        {
            private readonly ScriptSettingsManager _settingsManager;
            private readonly TempDirectory _secretsDirectory = new TempDirectory();
            private readonly Task _initializationTask;

            public Fixture()
            {
                EventGenerator = new TestSystemEventGenerator();
                _settingsManager = ScriptSettingsManager.Instance;

                TestFunctionRoot = Path.Combine(TestHelpers.FunctionsTestDirectory, "Functions");
                TestLogsRoot = Path.Combine(TestHelpers.FunctionsTestDirectory, "Logs");
                TestSecretsRoot = Path.Combine(TestHelpers.FunctionsTestDirectory, "Secrets");

                string testRoot = Path.Combine(TestFunctionRoot, Guid.NewGuid().ToString());

                SecretsPath = Path.Combine(TestSecretsRoot, Guid.NewGuid().ToString());
                Directory.CreateDirectory(SecretsPath);
                string logRoot = Path.Combine(TestLogsRoot, Guid.NewGuid().ToString(), @"Functions");
                Directory.CreateDirectory(logRoot);
                FunctionsLogDir = Path.Combine(logRoot, @"Function");
                Directory.CreateDirectory(FunctionsLogDir);

                // Add some secret files (both old and valid)
                File.WriteAllText(Path.Combine(SecretsPath, ScriptConstants.HostMetadataFileName), string.Empty);
                File.WriteAllText(Path.Combine(SecretsPath, "WebHookTrigger.json"), string.Empty);
                File.WriteAllText(Path.Combine(SecretsPath, "QueueTriggerToBlob.json"), string.Empty);
                File.WriteAllText(Path.Combine(SecretsPath, "Foo.json"), string.Empty);
                File.WriteAllText(Path.Combine(SecretsPath, "Bar.json"), string.Empty);
                File.WriteAllText(Path.Combine(SecretsPath, "Invalid.json"), string.Empty);

                // Add some old file directories
                CreateTestFunctionLogs(FunctionsLogDir, "Foo");
                CreateTestFunctionLogs(FunctionsLogDir, "Bar");
                CreateTestFunctionLogs(FunctionsLogDir, "Baz");
                CreateTestFunctionLogs(FunctionsLogDir, "Invalid");

                ScriptHostConfiguration config = new ScriptHostConfiguration
                {
                    RootScriptPath = @"TestScripts\Node",
                    RootLogPath = logRoot,
                    FileLoggingMode = FileLoggingMode.Always
                };

                ISecretsRepository repository = new FileSystemSecretsRepository(SecretsPath);
                ISecretManager secretManager = new SecretManager(_settingsManager, repository, null);
                WebHostSettings webHostSettings = new WebHostSettings();
                webHostSettings.SecretsPath = SecretsPath;

                var hostConfig = config.HostConfig;

                var testEventGenerator = new TestSystemEventGenerator();
                hostConfig.AddService<IEventGenerator>(EventGenerator);
                var mockEventManager = new Mock<IScriptEventManager>();
                var mockRouter = CreateRouter();
                var mockHostManager = new WebScriptHostManager(config, new TestSecretManagerFactory(secretManager), mockEventManager.Object, _settingsManager, webHostSettings, mockRouter, new DefaultLoggerFactoryBuilder());
                HostManager = mockHostManager;

                _initializationTask = EnsureInitialized();
                
            }

            internal Task InitializationTask => _initializationTask;

            private async Task EnsureInitialized()
            {
                SynchronizationContext.SetSynchronizationContext(null);
                await Task.Run(() => HostManager.RunAndBlock()).ConfigureAwait(false);

                await TestHelpers.Await(() => HostManager.State == ScriptHostState.Running, timeout: 30000).ConfigureAwait(false);

                // verify startup system trace logs
                string[] expectedPatterns = new string[]
                {
                    "Info Reading host configuration file",
                    "Info Host configuration file read",
                    "Info Host lock lease acquired by instance ID '(.+)'",
                    @"Info Generating ([0-9]+) job function\(s\)",
                    @"Info Starting Host \(HostId=function-tests-node, Version=(.+), ProcessId=[0-9]+, AppDomainId=[0-9]+, Debug=False, ConsecutiveErrors=0, StartupCount=1, FunctionsExtensionVersion=\)",
                    "Info WebJobs.Indexing Found the following functions:",
                    "Info The next 5 occurrences of the schedule will be:",
                    "Info WebJobs.Host Job host started",
                    "Error The following 1 functions are in error:"
                };

                foreach (string pattern in expectedPatterns)
                {
                    Assert.True(EventGenerator.Events.Any(p => Regex.IsMatch(p, pattern)), $"Expected trace event {pattern} not found.");
                }
            }

            public TestSystemEventGenerator EventGenerator { get; private set; }

            public WebScriptHostManager HostManager { get; private set; }

            public string FunctionsLogDir { get; private set; }

            public string SecretsPath { get; private set; }

            public string TestFunctionRoot { get; private set; }

            public string TestLogsRoot { get; private set; }

            public string TestSecretsRoot { get; private set; }

            internal IWebJobsRouter CreateRouter()
            {
                var mockRouter = new Mock<IWebJobsRouter>();

                IInlineConstraintResolver resolver = new Mock<IInlineConstraintResolver>().Object;
                var builder = new WebJobsRouteBuilder(resolver, new Mock<IWebJobsRouteHandler>().Object, string.Empty);
                mockRouter.Setup(r => r.CreateBuilder(It.IsAny<IWebJobsRouteHandler>(), It.IsAny<string>()))
                    .Returns(builder);

                return mockRouter.Object;
            }

            public void Dispose()
            {
                if (HostManager != null)
                {
                    HostManager.Stop();
                    HostManager.Dispose();
                }

                try
                {
                    if (Directory.Exists(TestHelpers.FunctionsTestDirectory))
                    {
                        Directory.Delete(TestHelpers.FunctionsTestDirectory, recursive: true);
                    }
                }
                catch
                {
                    // occasionally get file in use errors
                }

                _secretsDirectory.Dispose();
            }

            private void CreateTestFunctionLogs(string logRoot, string functionName)
            {
                string functionLogPath = Path.Combine(logRoot, functionName);
                FileTraceWriter traceWriter = new FileTraceWriter(functionLogPath, TraceLevel.Verbose);
                traceWriter.Verbose("Test log message");
                traceWriter.Flush();
            }

            public class TestSystemEventGenerator : IEventGenerator
            {
                private readonly object _syncLock = new object();

                public TestSystemEventGenerator()
                {
                    Events = new List<string>();
                }

                public List<string> Events { get; private set; }

                public void LogFunctionTraceEvent(TraceLevel level, string subscriptionId, string appName, string functionName, string eventName, string source, string details, string summary)
                {
                    var elements = new string[] { level.ToString(), subscriptionId, appName, functionName, eventName, source, summary, details };
                    string evt = string.Join(" ", elements.Where(p => !string.IsNullOrEmpty(p)));
                    lock (_syncLock)
                    {
                        Events.Add(evt);
                    }
                }

                public void LogFunctionMetricEvent(string subscriptionId, string appName, string functoinName, string eventName, long average, long minimum, long maximum, long count, DateTime eventTimestamp)
                {
                    throw new NotImplementedException();
                }

                public void LogFunctionExecutionEvent(string executionId, string siteName, int concurrency, string functionName, string invocationId, string executionStage, long executionTimeSpan, bool success)
                {
                    throw new NotImplementedException();
                }

                public void LogFunctionDetailsEvent(string siteName, string functionName, string inputBindings, string outputBindings, string scriptType, bool isDisabled)
                {
                    throw new NotImplementedException();
                }

                public void LogFunctionExecutionAggregateEvent(string siteName, string functionName, long executionTimeInMs, long functionStartedCount, long functionCompletedCount, long functionFailedCount)
                {
                    throw new NotImplementedException();
                }
            }
        }
    }
}