// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Routing;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Moq;
using Newtonsoft.Json.Linq;
using WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class WebScriptHostManagerTests : IClassFixture<WebScriptHostManagerTests.Fixture>, IDisposable
    {
        private readonly ScriptSettingsManager _settingsManager;
        private readonly TempDirectory _secretsDirectory = new TempDirectory();
        private WebScriptHostManagerTests.Fixture _fixture;

        // Some tests need their own manager that differs from the fixture.
        private WebScriptHostManager _manager;

        public WebScriptHostManagerTests(WebScriptHostManagerTests.Fixture fixture)
        {
            _fixture = fixture;
            _settingsManager = ScriptSettingsManager.Instance;
        }

        [Fact]
        public async Task FunctionInvoke_SystemTraceEventsAreEmitted()
        {
            _fixture.EventGenerator.Events.Clear();

            var host = _fixture.HostManager.Instance;
            var input = Guid.NewGuid().ToString();
            var parameters = new Dictionary<string, object>
            {
                { "input", input }
            };
            await host.CallAsync("ManualTrigger", parameters);

            Assert.Equal(4, _fixture.EventGenerator.Events.Count);
            Assert.True(_fixture.EventGenerator.Events[0].StartsWith("Info WebJobs.Execution Executing 'Functions.ManualTrigger' (Reason='This function was programmatically called via the host APIs.', Id="));
            Assert.True(_fixture.EventGenerator.Events[1].StartsWith("Info ManualTrigger Function started (Id="));
            Assert.True(_fixture.EventGenerator.Events[2].StartsWith("Info ManualTrigger Function completed (Success, Id="));
            Assert.True(_fixture.EventGenerator.Events[3].StartsWith("Info WebJobs.Execution Executed 'Functions.ManualTrigger' (Succeeded, Id="));

            // make sure the user log wasn't traced
            Assert.False(_fixture.EventGenerator.Events.Any(p => p.Contains("ManualTrigger function invoked!")));
        }

        [Fact]
        public void FunctionLogFilesArePurgedOnStartup()
        {
            var logDirs = Directory.EnumerateDirectories(_fixture.FunctionsLogDir).Select(p => Path.GetFileName(p).ToLowerInvariant()).ToArray();

            // Even if a function is invalid an not part of the active
            // loaded functions, we don't want to purge data for it
            Assert.True(logDirs.Contains("invalid"));

            Assert.False(logDirs.Contains("foo"));
            Assert.False(logDirs.Contains("bar"));
            Assert.False(logDirs.Contains("baz"));
        }

        [Fact]
        public void SecretFilesArePurgedOnStartup()
        {
            var secretFiles = Directory.EnumerateFiles(_fixture.SecretsPath).Select(p => Path.GetFileName(p)).OrderBy(p => p).ToArray();
            Assert.Equal(4, secretFiles.Length);

            Assert.Equal(ScriptConstants.HostMetadataFileName, secretFiles[0]);
            Assert.Equal("Invalid.json", secretFiles[1]);
            Assert.Equal("QueueTriggerToBlob.json", secretFiles[2]);
            Assert.Equal("WebHookTrigger.json", secretFiles[3]);
        }

        [Fact]
        public async Task EmptyHost_StartsSuccessfully()
        {
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
            ISecretManager secretManager = new SecretManager(_settingsManager, repository, NullTraceWriter.Instance, null);
            WebHostSettings webHostSettings = new WebHostSettings();
            webHostSettings.SecretsPath = _secretsDirectory.Path;

            ScriptHostManager hostManager = new WebScriptHostManager(config, new TestSecretManagerFactory(secretManager), _settingsManager, webHostSettings);

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
            string functionTestDir = Path.Combine(_fixture.TestFunctionRoot, Guid.NewGuid().ToString());
            Directory.CreateDirectory(functionTestDir);
            string logDir = Path.Combine(_fixture.TestLogsRoot, Guid.NewGuid().ToString());
            string secretsDir = Path.Combine(_fixture.TestSecretsRoot, Guid.NewGuid().ToString());

            ScriptHostConfiguration config = new ScriptHostConfiguration
            {
                RootLogPath = logDir,
                RootScriptPath = functionTestDir,
                FileLoggingMode = FileLoggingMode.Always,
            };

            ISecretsRepository repository = new FileSystemSecretsRepository(_secretsDirectory.Path);
            SecretManager secretManager = new SecretManager(_settingsManager, repository, NullTraceWriter.Instance, null);
            WebHostSettings webHostSettings = new WebHostSettings();
            webHostSettings.SecretsPath = _secretsDirectory.Path;

            var factoryMock = new Mock<IScriptHostFactory>();
            int count = 0;
            factoryMock.Setup(p => p.Create(It.IsAny<IScriptHostEnvironment>(), _settingsManager, config)).Callback(() =>
            {
                count++;
            }).Throws(new Exception("Kaboom!"));

            ScriptHostManager hostManager = new WebScriptHostManager(config, new TestSecretManagerFactory(secretManager), _settingsManager, webHostSettings, factoryMock.Object);

            Task runTask = Task.Run(() => hostManager.RunAndBlock());

            await TestHelpers.Await(() =>
            {
                return count > 3;
            });

            hostManager.Stop();
            Assert.Equal(ScriptHostState.Default, hostManager.State);

            // regression test: previously on multiple restarts we were recomposing
            // the writer on each restart, resulting in a nested chain of writers
            // increasing on each restart
            Assert.Equal(typeof(SystemTraceWriter), config.TraceWriter.GetType());
        }

        [Fact]
        public async Task OnTimeoutException_IgnoreToken_StopsManager()
        {
            var trace = new TestTraceWriter(TraceLevel.Info);

            await RunTimeoutExceptionTest(trace, handleCancellation: false);

            await TestHelpers.Await(() => !(_manager.State == ScriptHostState.Running));
            Assert.DoesNotContain(trace.Traces, t => t.Message.StartsWith("Done"));
            Assert.Contains(trace.Traces, t => t.Message.StartsWith("Timeout value of 00:00:03 exceeded by function 'Functions.TimeoutToken' (Id: "));
            Assert.Contains(trace.Traces, t => t.Message == "A function timeout has occurred. Host is shutting down.");
        }

        [Fact]
        public async Task OnTimeoutException_UsesToken_ManagerKeepsRunning()
        {
            var trace = new TestTraceWriter(TraceLevel.Info);

            await RunTimeoutExceptionTest(trace, handleCancellation: true);

            // wait a few seconds to make sure the manager doesn't die
            await Assert.ThrowsAsync<ApplicationException>(() => TestHelpers.Await(() => !(_manager.State == ScriptHostState.Running), timeout: 3000));
            Assert.Contains(trace.Traces, t => t.Message.StartsWith("Done"));
            Assert.Contains(trace.Traces, t => t.Message.StartsWith("Timeout value of 00:00:03 exceeded by function 'Functions.TimeoutToken' (Id: "));
            Assert.DoesNotContain(trace.Traces, t => t.Message == "A function timeout has occurred. Host is shutting down.");
        }

        [Fact]
        public void AddRouteDataToRequest_DoesNotAddRequestProperty_WhenRouteDataNull()
        {
            var mockRouteData = new Mock<IHttpRouteData>(MockBehavior.Strict);
            IDictionary<string, object> values = null;
            mockRouteData.Setup(p => p.Values).Returns(values);
            HttpRequestMessage request = new HttpRequestMessage();

            WebScriptHostManager.AddRouteDataToRequest(mockRouteData.Object, request);

            Assert.False(request.Properties.ContainsKey(HttpExtensionConstants.AzureWebJobsHttpRouteDataKey));
        }

        [Fact]
        public void AddRouteDataToRequest_AddsRequestProperty_WhenRouteDataNotNull()
        {
            var mockRouteData = new Mock<IHttpRouteData>(MockBehavior.Strict);
            IDictionary<string, object> values = new Dictionary<string, object>
            {
                { "p1", "abc" },
                { "p2", 123 },
                { "p3", null },
                { "p4", RouteParameter.Optional }
            };
            mockRouteData.Setup(p => p.Values).Returns(values);
            HttpRequestMessage request = new HttpRequestMessage();

            WebScriptHostManager.AddRouteDataToRequest(mockRouteData.Object, request);

            var result = (IDictionary<string, object>)request.Properties[HttpExtensionConstants.AzureWebJobsHttpRouteDataKey];
            Assert.Equal(result["p1"], "abc");
            Assert.Equal(result["p2"], 123);
            Assert.Equal(result["p3"], null);
            Assert.Equal(result["p4"], null);
        }

        private async Task RunTimeoutExceptionTest(TraceWriter trace, bool handleCancellation)
        {
            TimeSpan gracePeriod = TimeSpan.FromMilliseconds(5000);
            _manager = await CreateAndStartWebScriptHostManager(trace);

            string scenarioName = handleCancellation ? "useToken" : "ignoreToken";

            var args = new Dictionary<string, object>
            {
                { "input", scenarioName }
            };

            await Assert.ThrowsAsync<FunctionTimeoutException>(() => _manager.Instance.CallAsync("TimeoutToken", args));
        }

        private async Task<WebScriptHostManager> CreateAndStartWebScriptHostManager(TraceWriter traceWriter)
        {
            var functions = new Collection<string> { "TimeoutToken" };

            ScriptHostConfiguration config = new ScriptHostConfiguration()
            {
                RootScriptPath = $@"TestScripts\CSharp",
                TraceWriter = traceWriter,
                FileLoggingMode = FileLoggingMode.Always,
                Functions = functions,
                FunctionTimeout = TimeSpan.FromSeconds(3)
            };

            var manager = new WebScriptHostManager(config, new TestSecretManagerFactory(), _settingsManager, new WebHostSettings { SecretsPath = _secretsDirectory.Path });
            Task task = Task.Run(() => { manager.RunAndBlock(); });
            await TestHelpers.Await(() => manager.State == ScriptHostState.Running);

            return manager;
        }

        public void Dispose()
        {
            if (_manager != null)
            {
                _manager.Stop();
                _manager.Dispose();
            }

            _secretsDirectory.Dispose();
        }

        public class Fixture : IDisposable
        {
            private readonly ScriptSettingsManager _settingsManager;
            private readonly TempDirectory _secretsDirectory = new TempDirectory();

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
                ISecretManager secretManager = new SecretManager(_settingsManager, repository, NullTraceWriter.Instance, null);
                WebHostSettings webHostSettings = new WebHostSettings();
                webHostSettings.SecretsPath = SecretsPath;

                var hostConfig = config.HostConfig;
                var testEventGenerator = new TestSystemEventGenerator();
                hostConfig.AddService<IEventGenerator>(EventGenerator);
                var mockHostManager = new WebScriptHostManager(config, new TestSecretManagerFactory(secretManager), _settingsManager, webHostSettings);
                HostManager = mockHostManager;
                Task task = Task.Run(() => { HostManager.RunAndBlock(); });

                TestHelpers.Await(() =>
                {
                    return HostManager.State == ScriptHostState.Running;
                }).GetAwaiter().GetResult();

                // verify startup system trace logs
                string[] expectedPatterns = new string[]
                {
                    "Info Reading host configuration file",
                    "Info Host lock lease acquired by instance ID '(.+)'",
                    "Info Function 'Excluded' is marked as excluded",
                    @"Info Generating ([0-9]+) job function\(s\)",
                    @"Info Starting Host \(HostId=function-tests-node, Version=(.+), ProcessId=[0-9]+, Debug=False, Attempt=0\)",
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

                public void LogFunctionTraceEvent(TraceLevel level, string subscriptionId, string appName, string functionName, string eventName, string source, string details, string summary, Exception exception = null)
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