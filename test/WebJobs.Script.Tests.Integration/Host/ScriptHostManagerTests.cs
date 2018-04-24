// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Scale;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Microsoft.WindowsAzure.Storage.Blob;
using Moq;
using Moq.Protected;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class ScriptHostManagerTests
    {
        private const string ID = "5a709861cab44e68bfed5d2c2fe7fc0c";
        private readonly ScriptSettingsManager _settingsManager;

        public ScriptHostManagerTests()
        {
            _settingsManager = ScriptSettingsManager.Instance;
        }

        //// Update a script file (the function.json) to force the ScriptHost to re-index and pick up new changes.
        //// Test with timers:
        [Fact]
        public async Task UpdateFileAndRestart()
        {
            var fixture = new NodeScriptHostTests.TestFixture(false);
            var config = fixture.Host.ScriptConfig;

            config.OnConfigurationApplied = c =>
            {
                c.Functions = new Collection<string> { "TimerTrigger" };
            };

            var blob1 = await UpdateOutputName("testblob", "first", fixture);

            using (var eventManager = new ScriptEventManager())
            using (var manager = new ScriptHostManager(config, eventManager))
            {
                string GetErrorTraces()
                {
                    var messages = fixture.LoggerProvider.GetAllLogMessages()
                        .Where(t => t.Level == LogLevel.Error)
                        .Select(t => t.FormattedMessage);

                    return string.Join(Environment.NewLine, messages);
                }

                List<Exception> exceptions = new List<Exception>();

                // Background task to run while the main thread is pumping events at RunAndBlock().
                Thread backgroundThread = new Thread(_ =>
                {
                    try
                    {
                        // don't start until the manager is running
                        TestHelpers.Await(() => manager.State == ScriptHostState.Running,
                            userMessageCallback: GetErrorTraces).GetAwaiter().GetResult();

                        // Wait for initial execution.
                        TestHelpers.Await(async () =>
                         {
                             bool exists = await blob1.ExistsAsync();
                             return exists;
                         }, timeout: 10 * 1000, userMessageCallback: GetErrorTraces).GetAwaiter().GetResult();

                        // This changes the bindings so that we now write to blob2
                        var blob2 = UpdateOutputName("first", "testblob", fixture).Result;

                        // wait for newly executed
                        TestHelpers.Await(async () =>
                         {
                             bool exists = await blob2.ExistsAsync();
                             return exists;
                         }, timeout: 30 * 1000, userMessageCallback: GetErrorTraces).GetAwaiter().GetResult();

                        // The TimerTrigger can fire before the host is fully started. To be more
                        // reliably clean up the test, wait until it is running before calling Stop.
                        TestHelpers.Await(() => manager.State == ScriptHostState.Running,
                            userMessageCallback: GetErrorTraces).GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        exceptions.Add(ex);
                    }
                    finally
                    {
                        try
                        {
                            // Calling Stop (rather than using a token) lets us wait until all listeners have stopped.
                            manager.Stop();
                        }
                        catch (Exception ex)
                        {
                            exceptions.Add(ex);
                        }
                    }
                });

                try
                {
                    backgroundThread.Start();
                    manager.RunAndBlock();
                    Assert.True(backgroundThread.Join(60000), "The background task did not complete in 60 seconds.");

                    string exceptionString = string.Join(Environment.NewLine, exceptions.Select(p => p.ToString()));
                    Assert.True(exceptions.Count() == 0, exceptionString);
                }
                finally
                {
                    // make sure to put the original names back
                    await UpdateOutputName("first", "testblob", fixture);
                }
            }
        }

        [Fact]
        public async Task RenameFunctionAndRestart()
        {
            var oldDirectory = Path.Combine(Directory.GetCurrentDirectory(), "TestScripts/Node/TimerTrigger");
            var newDirectory = Path.Combine(Directory.GetCurrentDirectory(), "TestScripts/Node/MovedTrigger");

            var fixture = new NodeScriptHostTests.TestFixture(false);
            var config = fixture.Host.ScriptConfig;

            config.OnConfigurationApplied = c =>
            {
                c.Functions = new Collection<string> { "TimerTrigger", "MovedTrigger" };
            };

            var blob = fixture.TestOutputContainer.GetBlockBlobReference("testblob");
            await blob.DeleteIfExistsAsync();
            var mockEnvironment = new Mock<IScriptHostEnvironment>();

            using (var eventManager = new ScriptEventManager())
            using (var manager = new ScriptHostManager(config, eventManager, mockEnvironment.Object))
            using (var resetEvent = new ManualResetEventSlim())
            {
                List<Exception> exceptions = new List<Exception>();

                mockEnvironment.Setup(e => e.RestartHost())
                    .Callback(() =>
                    {
                        resetEvent.Set();
                        manager.RestartHost();
                    });

                // Background task to run while the main thread is pumping events at RunAndBlock().
                Thread backgroundThread = new Thread(_ =>
                {
                    try
                    {
                        // don't start until the manager is running
                        TestHelpers.Await(() => manager.State == ScriptHostState.Running,
                        userMessageCallback: () => "Host did not start in time.").GetAwaiter().GetResult();

                        // Wait for initial execution.
                        TestHelpers.Await(async () =>
                        {
                            bool exists = await blob.ExistsAsync();
                            return exists;
                        }, timeout: 10 * 1000,
                        userMessageCallback: () => $"Blob '{blob.Uri}' was not created by 'TimerTrigger' in time.").GetAwaiter().GetResult();

                        // find __dirname from blob
                        string text;
                        using (var stream = new MemoryStream())
                        {
                            blob.DownloadToStreamAsync(stream).Wait();
                            text = System.Text.Encoding.UTF8.GetString(stream.ToArray());
                        }

                        Assert.Contains("TimerTrigger", text);

                        // rename directory & delete old blob
                        Directory.Move(oldDirectory, newDirectory);

                        resetEvent.Wait(TimeSpan.FromSeconds(10));

                        blob.DeleteIfExistsAsync().GetAwaiter().GetResult();

                        // wait for newly executed
                        TestHelpers.Await(async () =>
                        {
                            bool exists = await blob.ExistsAsync();
                            return exists;
                        }, timeout: 30 * 1000,
                        userMessageCallback: () => $"Blob '{blob.Uri}' was not created by 'MovedTrigger' in time.").GetAwaiter().GetResult();

                        using (var stream = new MemoryStream())
                        {
                            blob.DownloadToStreamAsync(stream).Wait();
                            text = System.Text.Encoding.UTF8.GetString(stream.ToArray());
                        }

                        Assert.Contains("MovedTrigger", text);

                        // The TimerTrigger can fire before the host is fully started. To be more
                        // reliably clean up the test, wait until it is running before calling Stop.
                        TestHelpers.Await(() => manager.State == ScriptHostState.Running).GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        exceptions.Add(ex);
                    }
                    finally
                    {
                        try
                        {
                            manager.Stop();
                        }
                        catch (Exception ex)
                        {
                            exceptions.Add(ex);
                        }
                    }
                });

                try
                {
                    backgroundThread.Start();
                    manager.RunAndBlock();
                    Assert.True(backgroundThread.Join(60000), "The background task did not complete in 60 seconds.");

                    string exceptionString = string.Join(Environment.NewLine, exceptions.Select(p => p.ToString()));
                    Assert.True(exceptions.Count() == 0, exceptionString);
                }
                finally
                {
                    // Move the directory back after the host has stopped to prevent
                    // unnecessary host restarts
                    if (Directory.Exists(newDirectory))
                    {
                        Directory.Move(newDirectory, oldDirectory);
                    }
                }
            }
        }

        [Fact]
        public void RunAndBlock_DisposesOfHost_WhenExceptionIsThrown()
        {
            ScriptHostConfiguration config = new ScriptHostConfiguration()
            {
                RootScriptPath = Environment.CurrentDirectory
            };

            var eventManager = new Mock<IScriptEventManager>();
            var hostMock = new Mock<ScriptHost>(new NullScriptHostEnvironment(), eventManager.Object, config, null, null, null);
            var factoryMock = new Mock<IScriptHostFactory>();
            factoryMock.Setup(f => f.Create(It.IsAny<IScriptHostEnvironment>(), It.IsAny<IScriptEventManager>(), _settingsManager, It.IsAny<ScriptHostConfiguration>(), It.IsAny<ILoggerProviderFactory>()))
                .Returns(hostMock.Object);

            var target = new Mock<ScriptHostManager>(config, _settingsManager, factoryMock.Object, eventManager.Object, new NullScriptHostEnvironment(), null, null);
            target.Protected().Setup("OnHostStarted")
                .Throws(new Exception());

            hostMock.Protected().Setup("Dispose", true)
                .Callback(() => target.Object.Stop());

            Task.Run(() => target.Object.RunAndBlock()).Wait(50000);

            hostMock.Protected().Verify("Dispose", Times.Once(), true);
        }

        [Fact]
        public async Task RunAndBlock_HostJsonValueError_LogsError()
        {
            // Try to load valid host.json file that has an out-of-range value.
            // Ensure that it's logged to ILogger

            string rootPath = Path.Combine(Environment.CurrentDirectory, @"TestScripts\OutOfRange");

            ScriptHostConfiguration config = new ScriptHostConfiguration()
            {
                RootScriptPath = rootPath
            };

            TestLoggerProvider provider = new TestLoggerProvider();
            var loggerProviderFactory = new TestLoggerProviderFactory(provider, includeDefaultLoggerProviders: false);

            var factoryMock = new Mock<IScriptHostFactory>();
            var scriptHostFactory = new TestScriptHostFactory();
            var eventManagerMock = new Mock<IScriptEventManager>();
            var hostManager = new ScriptHostManager(config, _settingsManager, scriptHostFactory, eventManagerMock.Object, loggerProviderFactory: loggerProviderFactory);
            Task taskIgnore = Task.Run(() => hostManager.RunAndBlock());

            await TestHelpers.Await(() => hostManager.State == ScriptHostState.Error, 3000, 50);

            Assert.Equal(ScriptHostState.Error, hostManager.State);
            Assert.False(hostManager.CanInvoke());

            hostManager.Stop();
            var ex = hostManager.LastError;
            Assert.True(ex is ArgumentOutOfRangeException);

            string msg = "A ScriptHost error has occurred";

            var startupLogger = provider.CreatedLoggers.Last();
            var loggerMessage = startupLogger.GetLogMessages().First();
            Assert.Equal(msg, loggerMessage.FormattedMessage);
            Assert.Same(ex, loggerMessage.Exception);
        }

        [Fact]
        public async Task RunAndBlock_ParseError_LogsError()
        {
            TestLoggerProvider loggerProvider = new TestLoggerProvider();
            TestLoggerProviderFactory factory = new TestLoggerProviderFactory(loggerProvider, includeDefaultLoggerProviders: false);

            string rootPath = Path.Combine(Environment.CurrentDirectory, "ScriptHostTests");
            if (!Directory.Exists(rootPath))
            {
                Directory.CreateDirectory(rootPath);
            }

            File.WriteAllText(Path.Combine(rootPath, "host.json"), @"{<unparseable>}");

            var config = new ScriptHostConfiguration()
            {
                RootScriptPath = rootPath
            };
            config.HostConfig.HostId = ID;

            var scriptHostFactory = new TestScriptHostFactory();
            var eventManagerMock = new Mock<IScriptEventManager>();
            var hostManager = new ScriptHostManager(config, _settingsManager, scriptHostFactory, eventManagerMock.Object, loggerProviderFactory: factory);
            Task taskIgnore = Task.Run(() => hostManager.RunAndBlock());

            await TestHelpers.Await(() => hostManager.State == ScriptHostState.Error, 3000, 50);

            Assert.Equal(ScriptHostState.Error, hostManager.State);

            hostManager.Stop();

            var ex = hostManager.LastError;
            Assert.True(ex is FormatException);
            Assert.Equal("Unable to parse host.json file.", ex.Message);

            var logger = loggerProvider.CreatedLoggers.Last();
            Assert.StartsWith("A ScriptHost error has occurred", logger.GetLogMessages()[1].FormattedMessage);
            Assert.Equal("Unable to parse host.json file.", logger.GetLogMessages()[1].Exception.Message);
        }

        [Fact]
        public async Task HostHealthMonitor_TriggersShutdown_WhenHostUnhealthy()
        {
            string functionDir = Path.Combine(TestHelpers.FunctionsTestDirectory, "Functions", Guid.NewGuid().ToString());
            Directory.CreateDirectory(functionDir);
            string logDir = Path.Combine(TestHelpers.FunctionsTestDirectory, "Logs", Guid.NewGuid().ToString());

            JObject hostConfig = new JObject
            {
                { "id", "123456" }
            };
            File.WriteAllText(Path.Combine(functionDir, ScriptConstants.HostMetadataFileName), hostConfig.ToString());

            var config = new ScriptHostConfiguration
            {
                RootScriptPath = functionDir,
                RootLogPath = logDir,
                FileLoggingMode = FileLoggingMode.Always,
            };

            // configure the monitor so it will fail within a couple seconds
            config.HostHealthMonitor.HealthCheckInterval = TimeSpan.FromMilliseconds(100);
            config.HostHealthMonitor.HealthCheckWindow = TimeSpan.FromSeconds(1);
            config.HostHealthMonitor.HealthCheckThreshold = 5;

            var environmentMock = new Mock<IScriptHostEnvironment>(MockBehavior.Strict);
            environmentMock.Setup(p => p.Shutdown());

            var mockSettings = new Mock<ScriptSettingsManager>(null);
            mockSettings.Setup(p => p.IsAppServiceEnvironment).Returns(true);

            var eventManagerMock = new Mock<IScriptEventManager>();
            var hostHealthConfig = new HostHealthMonitorConfiguration();
            var mockHostPerformanceManager = new Mock<HostPerformanceManager>(mockSettings.Object, hostHealthConfig);

            bool underHighLoad = false;
            mockHostPerformanceManager.Setup(p => p.IsUnderHighLoad(It.IsAny<Collection<string>>(), It.IsAny<ILogger>()))
                .Callback<Collection<string>, ILogger>((c, l) =>
                {
                    c.Add("Connections");
                })
                .Returns(() => underHighLoad);

            var loggerProvider = new TestLoggerProvider();
            var loggerProviderFactory = new TestLoggerProviderFactory(loggerProvider);
            var hostManager = new ScriptHostManager(config, mockSettings.Object, new ScriptHostFactory(), eventManagerMock.Object, environmentMock.Object, loggerProviderFactory, mockHostPerformanceManager.Object);
            Assert.True(hostManager.ShouldMonitorHostHealth);
            Task runTask = Task.Run(() => hostManager.RunAndBlock());
            await TestHelpers.Await(() => hostManager.State == ScriptHostState.Running);

            // now that host is running make host unhealthy and wait
            // for host shutdown
            underHighLoad = true;

            await TestHelpers.Await(() => hostManager.State == ScriptHostState.Error);

            Assert.Equal(ScriptHostState.Error, hostManager.State);
            environmentMock.Verify(p => p.Shutdown(), Times.Once);

            // we expect a few restart iterations
            var thresholdErrors = loggerProvider.GetAllLogMessages().Where(p => p.Exception is InvalidOperationException && p.Exception.Message == "Host thresholds exceeded: [Connections]. For more information, see https://aka.ms/functions-thresholds.");
            Assert.True(thresholdErrors.Count() > 1);

            var log = loggerProvider.GetAllLogMessages().Last();
            Assert.True(loggerProvider.GetAllLogMessages().Count(p => p.FormattedMessage == "Host is unhealthy. Initiating a restart." && p.Level == LogLevel.Error) > 0);
            Assert.Equal("Host unhealthy count exceeds the threshold of 5 for time window 00:00:01. Initiating shutdown.", log.FormattedMessage);
            Assert.Equal(LogLevel.Error, log.Level);
        }

        [Fact(Skip = "Fix this")]
        public async Task RunAndBlock_SetsLastError_WhenExceptionIsThrown()
        {
            ScriptHostConfiguration config = new ScriptHostConfiguration()
            {
                RootScriptPath = @"TestScripts\Empty"
            };

            var factoryMock = new Mock<IScriptHostFactory>();
            var scriptHostFactory = new TestScriptHostFactory()
            {
                Throw = true
            };
            var eventManagerMock = new Mock<IScriptEventManager>();
            var hostManager = new ScriptHostManager(config, _settingsManager, scriptHostFactory, eventManagerMock.Object);
            Task taskIgnore = Task.Run(() => hostManager.RunAndBlock());

            // we expect a host exception immediately
            await Task.Delay(2000);

            Assert.Equal(ScriptHostState.Error, hostManager.State);
            Assert.False(hostManager.CanInvoke());
            Assert.NotNull(hostManager.LastError);
            Assert.Equal("Kaboom!", hostManager.LastError.Message);

            // now verify that if no error is thrown on the next iteration
            // the cached error is cleared
            scriptHostFactory.Throw = false;
            await TestHelpers.Await(() =>
            {
                return hostManager.State == ScriptHostState.Running;
            });

            Assert.Null(hostManager.LastError);
            Assert.True(hostManager.CanInvoke());
            Assert.Equal(ScriptHostState.Running, hostManager.State);
        }

        [Fact]
        public void IsHostHealthy_ReturnsExpectedResult()
        {
            var config = new ScriptHostConfiguration()
            {
                RootScriptPath = Environment.CurrentDirectory
            };

            var mockSettings = new Mock<ScriptSettingsManager>(MockBehavior.Strict, null);
            var eventManager = new Mock<IScriptEventManager>();
            var hostMock = new Mock<ScriptHost>(new NullScriptHostEnvironment(), eventManager.Object, config, null, null, null);
            var factoryMock = new Mock<IScriptHostFactory>();
            factoryMock.Setup(f => f.Create(It.IsAny<IScriptHostEnvironment>(), It.IsAny<IScriptEventManager>(), mockSettings.Object, It.IsAny<ScriptHostConfiguration>(), It.IsAny<ILoggerProviderFactory>()))
                .Returns(hostMock.Object);

            var hostHealthConfig = new HostHealthMonitorConfiguration();
            var mockHostPerformanceManager = new Mock<HostPerformanceManager>(mockSettings.Object, hostHealthConfig);
            var target = new Mock<ScriptHostManager>(config, mockSettings.Object, factoryMock.Object, eventManager.Object, new NullScriptHostEnvironment(), null, mockHostPerformanceManager.Object);

            Collection<string> exceededCounters = new Collection<string>();
            bool isUnderHighLoad = false;
            mockHostPerformanceManager.Setup(p => p.IsUnderHighLoad(It.IsAny<Collection<string>>(), It.IsAny<ILogger>()))
                .Callback<Collection<string>, ILogger>((c, t) =>
                {
                    foreach (var counter in exceededCounters)
                    {
                        c.Add(counter);
                    }
                })
                .Returns(() => isUnderHighLoad);

            bool isAzureEnvironment = false;
            mockSettings.Setup(p => p.IsAppServiceEnvironment).Returns(() => isAzureEnvironment);
            mockSettings.Setup(p => p.FileSystemIsReadOnly).Returns(false);

            config.HostHealthMonitor.Enabled = false;
            Assert.True(target.Object.IsHostHealthy());

            config.HostHealthMonitor.Enabled = true;
            Assert.True(target.Object.IsHostHealthy());

            isAzureEnvironment = true;
            Assert.True(target.Object.IsHostHealthy());

            isUnderHighLoad = true;
            exceededCounters.Add("Foo");
            exceededCounters.Add("Bar");
            Assert.False(target.Object.IsHostHealthy());

            var ex = Assert.Throws<InvalidOperationException>(() => target.Object.IsHostHealthy(true));
            Assert.Equal("Host thresholds exceeded: [Foo, Bar]. For more information, see https://aka.ms/functions-thresholds.", ex.Message);
        }

        [Fact]
        public async Task EmptyHost_StartsSuccessfully()
        {
            string functionDir = Path.Combine(TestHelpers.FunctionsTestDirectory, "Functions", Guid.NewGuid().ToString());
            Directory.CreateDirectory(functionDir);

            // important for the repro that this directory does not exist
            string logDir = Path.Combine(TestHelpers.FunctionsTestDirectory, "Logs", Guid.NewGuid().ToString());

            JObject hostConfig = new JObject
            {
                { "id", "123456" }
            };
            File.WriteAllText(Path.Combine(functionDir, ScriptConstants.HostMetadataFileName), hostConfig.ToString());

            ScriptHostConfiguration config = new ScriptHostConfiguration
            {
                RootScriptPath = functionDir,
                RootLogPath = logDir,
                FileLoggingMode = FileLoggingMode.Always
            };

            var eventManagerMock = new Mock<IScriptEventManager>();
            ScriptHostManager hostManager = new ScriptHostManager(config, eventManagerMock.Object);

            // start the host and wait for it to be running
            Task runTask = Task.Run(() => hostManager.RunAndBlock());
            await TestHelpers.Await(() => hostManager.State == ScriptHostState.Running, timeout: 10000);

            // exercise restart
            hostManager.RestartHost();
            Assert.Equal(ScriptHostState.Default, hostManager.State);
            await TestHelpers.Await(() => hostManager.State == ScriptHostState.Running, timeout: 10000);

            // stop the host fully
            hostManager.Stop();
            Assert.Equal(ScriptHostState.Default, hostManager.State);

            await Task.Delay(FileWriter.LogFlushIntervalMs);

            string hostLogFilePath = Directory.EnumerateFiles(Path.Combine(logDir, "Host")).Single();
            string hostLogs = File.ReadAllText(hostLogFilePath);

            Assert.Contains("Generating 0 job function(s)", hostLogs);
            Assert.Contains("No job functions found.", hostLogs);
            Assert.Contains("Job host started", hostLogs);
            Assert.Contains("Job host stopped", hostLogs);
        }

        // Update the manifest for the timer function
        // - this will cause a file touch which cause ScriptHostManager to notice and update
        // - set to a new output location so that we can ensure we're getting new changes.
        private static async Task<CloudBlockBlob> UpdateOutputName(string prev, string hint, ScriptHostEndToEndTestFixture fixture)
        {
            string name = hint;

            // As soon as we touch the file, the trigger may reload, so delete any existing blob first.
            var blob = fixture.TestOutputContainer.GetBlockBlobReference(name);
            await blob.DeleteIfExistsAsync();

            string manifestPath = Path.Combine(Environment.CurrentDirectory, @"TestScripts\Node\TimerTrigger\function.json");
            string content = File.ReadAllText(manifestPath);
            content = content.Replace(prev, name);
            File.WriteAllText(manifestPath, content);

            return blob;
        }

        private class TestScriptHostFactory : IScriptHostFactory
        {
            public bool Throw { get; set; }

            public ScriptHost Create(IScriptHostEnvironment environment, IScriptEventManager eventManager, ScriptSettingsManager settingsManager, ScriptHostConfiguration config, ILoggerProviderFactory loggerProviderFactory)
            {
                if (Throw)
                {
                    throw new Exception("Kaboom!");
                }

                var mockMetricsLogger = new Mock<IMetricsLogger>(MockBehavior.Strict);
                config.HostConfig.AddService<IMetricsLogger>(mockMetricsLogger.Object);
                mockMetricsLogger.Setup(p => p.BeginEvent(It.IsAny<string>(), It.IsAny<string>())).Returns(new object());
                mockMetricsLogger.Setup(p => p.EndEvent(It.IsAny<object>()));
                mockMetricsLogger.Setup(p => p.LogEvent(It.IsAny<string>(), It.IsAny<string>()));
                mockMetricsLogger.Setup(p => p.LogEvent(It.IsAny<MetricEvent>()));

                return new ScriptHost(environment, eventManager, config, settingsManager, loggerProviderFactory);
            }
        }
    }
}