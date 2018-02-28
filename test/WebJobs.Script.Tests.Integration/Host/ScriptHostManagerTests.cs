// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Scale;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage.Blob;
using Moq;
using Moq.Protected;
using Newtonsoft.Json.Linq;
using Xunit;
using static Microsoft.Azure.WebJobs.Script.FunctionTraceWriterFactory;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    [Trait("Category", "E2E")]
    [Trait("E2E", nameof(ScriptHostManagerTests))]
    public class ScriptHostManagerTests
    {
        private const string ID = "5a709861cab44e68bfed5d2c2fe7fc0c";
        private readonly ScriptSettingsManager _settingsManager;

        public ScriptHostManagerTests()
        {
            _settingsManager = ScriptSettingsManager.Instance;
        }

        // Update a script file (the function.json) to force the ScriptHost to re-index and pick up new changes.
        // Test with timers:
        [Fact]
        public async Task UpdateFileAndRestart()
        {
            CancellationTokenSource cts = new CancellationTokenSource();
            var fixture = new NodeEndToEndTests.TestFixture();
            var blob1 = UpdateOutputName("testblob", "first", fixture);

            await fixture.Host.StopAsync();
            var config = fixture.Host.ScriptConfig;

            ExceptionDispatchInfo exception = null;
            ExceptionDispatchInfo updateException = null;
            using (var eventManager = new ScriptEventManager())
            using (var manager = new ScriptHostManager(config, eventManager))
            {
                // Background task to run while the main thread is pumping events at RunAndBlock().
                Thread t = new Thread(_ =>
                {
                    try
                    {
                        // don't start until the manager is running
                        TestHelpers.Await(() => manager.State == ScriptHostState.Running).Wait();

                        // Wait for initial execution.
                        TestHelpers.Await(() =>
                        {
                            bool exists = blob1.Exists();
                            return exists;
                        }, timeout: 10 * 1000).Wait();

                        // This changes the bindings so that we now write to blob2
                        var blob2 = UpdateOutputName("first", "testblob", fixture);

                        // wait for newly executed
                        TestHelpers.Await(() =>
                        {
                            bool exists = blob2.Exists();
                            return exists;
                        }, timeout: 30 * 1000).Wait();
                    }
                    catch (Exception ex)
                    {
                        exception = ExceptionDispatchInfo.Capture(ex);
                    }
                    finally
                    {
                        try
                        {
                            UpdateOutputName("first", "testblob", fixture);
                        }
                        catch (Exception ex)
                        {
                            updateException = ExceptionDispatchInfo.Capture(ex);
                        }
                    }

                    cts.Cancel();
                });
                t.Start();

                manager.RunAndBlock(cts.Token);

                t.Join();

                Assert.True(exception == null, exception?.SourceException?.ToString());
                Assert.True(updateException == null, "Failed to update output name: " + updateException?.SourceException?.ToString());
            }
        }

        [Fact]
        public async Task RenameFunctionAndRestart()
        {
            var oldDirectory = Path.Combine(Directory.GetCurrentDirectory(), "TestScripts/Node/TimerTrigger");
            var newDirectory = Path.Combine(Directory.GetCurrentDirectory(), "TestScripts/Node/MovedTrigger");

            CancellationTokenSource cts = new CancellationTokenSource();

            // Don't wait forever
            bool timeout = false;
            Task ignore = Task.Delay(TimeSpan.FromMinutes(3)).ContinueWith((t) =>
            {
                timeout = true;
                cts.Cancel();
            });

            var fixture = new NodeEndToEndTests.TestFixture();
            await fixture.Host.StopAsync();
            var config = fixture.Host.ScriptConfig;

            var blob = fixture.TestOutputContainer.GetBlockBlobReference("testblob");

            ExceptionDispatchInfo exception = null;
            ExceptionDispatchInfo moveException = null;
            var mockEnvironment = new Mock<IScriptHostEnvironment>();
            using (var eventManager = new ScriptEventManager())
            using (var manager = new ScriptHostManager(config, eventManager, mockEnvironment.Object))
            using (var resetEvent = new ManualResetEventSlim())
            {
                mockEnvironment.Setup(e => e.RestartHost())
                    .Callback(() =>
                    {
                        resetEvent.Set();
                        manager.RestartHost();
                    });

                // Background task to run while the main thread is pumping events at RunAndBlock().
                Thread t = new Thread(_ =>
                {
                    try
                    {
                        // don't start until the manager is running
                        TestHelpers.Await(() => manager.State == ScriptHostState.Running).Wait();

                        // Wait for initial execution.
                        TestHelpers.Await(() =>
                        {
                            bool exists = blob.Exists();
                            return exists;
                        }, timeout: 10 * 1000).Wait();

                        // find __dirname from blob
                        string text;
                        using (var stream = new MemoryStream())
                        {
                            blob.DownloadToStream(stream);
                            text = System.Text.Encoding.UTF8.GetString(stream.ToArray());
                        }

                        Assert.Contains("TimerTrigger", text);

                        // rename directory & delete old blob
                        Directory.Move(oldDirectory, newDirectory);

                        Assert.True(resetEvent.Wait(TimeSpan.FromSeconds(10)), "Timeout waiting for 'RestartHost' to be called.");

                        blob.Delete();

                        // wait for newly executed
                        TestHelpers.Await(() =>
                        {
                            bool exists = blob.Exists();
                            return exists;
                        }, timeout: 30 * 1000).Wait();

                        using (var stream = new MemoryStream())
                        {
                            blob.DownloadToStream(stream);
                            text = System.Text.Encoding.UTF8.GetString(stream.ToArray());
                        }

                        Assert.Contains("MovedTrigger", text);
                    }
                    catch (Exception ex)
                    {
                        exception = ExceptionDispatchInfo.Capture(ex);
                    }
                    finally
                    {
                        try
                        {
                            Directory.Move(newDirectory, oldDirectory);
                        }
                        catch (Exception ex)
                        {
                            moveException = ExceptionDispatchInfo.Capture(ex);
                        }
                    }

                    cts.Cancel();
                });

                t.Start();

                manager.RunAndBlock(cts.Token);

                Assert.True(t.Join(TimeSpan.FromSeconds(10)), "Thread join timed out.");

                Assert.False(timeout, "The test timed out and was canceled.");
                Assert.True(exception == null, exception?.SourceException?.ToString());
                Assert.True(moveException == null, "Failed to move function back to original directory: " + moveException?.SourceException?.ToString());
            }
        }

        [Fact]
        public void RunAndBlock_DisposesOfHost_WhenExceptionIsThrown()
        {
            ScriptHostConfiguration config = new ScriptHostConfiguration()
            {
                RootScriptPath = Environment.CurrentDirectory,
                TraceWriter = new TestTraceWriter(TraceLevel.Verbose)
            };

            var eventManager = new Mock<IScriptEventManager>();
            var hostMock = new Mock<ScriptHost>(new NullScriptHostEnvironment(), eventManager.Object, config, null, null);
            var factoryMock = new Mock<IScriptHostFactory>();
            factoryMock.Setup(f => f.Create(It.IsAny<IScriptHostEnvironment>(), It.IsAny<IScriptEventManager>(), _settingsManager, It.IsAny<ScriptHostConfiguration>()))
                .Returns(hostMock.Object);

            var target = new Mock<ScriptHostManager>(config, _settingsManager, factoryMock.Object, eventManager.Object, new NullScriptHostEnvironment(), null);
            target.Protected().Setup("OnHostStarted")
                .Throws(new Exception());

            hostMock.Protected().Setup("Dispose", true)
                .Callback(() => target.Object.Stop());

            Task.Run(() => target.Object.RunAndBlock()).Wait(5000);

            hostMock.Protected().Verify("Dispose", Times.Once(), true);
        }

        [Fact]
        public async Task RunAndBlock_HostJsonValueError_LogsError()
        {
            // Try to load valid host.json file that has an out-of-range value.
            // Ensure that it's logged to TraceWriter and ILogger

            var traceWriter = new TestTraceWriter(TraceLevel.Verbose);
            string rootPath = Path.Combine(Environment.CurrentDirectory, @"TestScripts\OutOfRange");

            ScriptHostConfiguration config = new ScriptHostConfiguration()
            {
                RootScriptPath = rootPath,
                TraceWriter = traceWriter
            };

            TestLoggerProvider provider = new TestLoggerProvider();
            config.LoggerFactoryBuilder = new TestLoggerFactoryBuilder(provider);

            var factoryMock = new Mock<IScriptHostFactory>();
            var scriptHostFactory = new TestScriptHostFactory();
            var eventManagerMock = new Mock<IScriptEventManager>();
            var hostManager = new ScriptHostManager(config, _settingsManager, scriptHostFactory, eventManagerMock.Object);
            Task taskIgnore = Task.Run(() => hostManager.RunAndBlock());

            await TestHelpers.Await(() => hostManager.State == ScriptHostState.Error, 3000);

            Assert.Equal(ScriptHostState.Error, hostManager.State);
            Assert.False(hostManager.CanInvoke());
            hostManager.Stop();
            var ex = hostManager.LastError;
            Assert.True(ex is ArgumentOutOfRangeException);

            string msg = "A ScriptHost error has occurred";
            var trace = traceWriter.GetTraces().Last(t => t.Level == TraceLevel.Error);
            Assert.Equal(msg, trace.Message);
            Assert.Same(ex, trace.Exception);

            var startupLogger = provider.CreatedLoggers.Last();
            var loggerMessage = startupLogger.LogMessages.First();
            Assert.Equal(msg, loggerMessage.FormattedMessage);
            Assert.Same(ex, loggerMessage.Exception);
        }

        [Fact]
        public async Task RunAndBlock_ParseError_LogsError()
        {
            TestLoggerProvider loggerProvider = null;
            var loggerFactoryHookMock = new Mock<ILoggerFactoryBuilder>(MockBehavior.Strict);
            loggerFactoryHookMock
                .Setup(m => m.AddLoggerProviders(It.IsAny<ILoggerFactory>(), It.IsAny<ScriptHostConfiguration>(), It.IsAny<ScriptSettingsManager>()))
                .Callback<ILoggerFactory, ScriptHostConfiguration, ScriptSettingsManager>((factory, scriptConfig, settings) =>
                {
                    loggerProvider = new TestLoggerProvider(scriptConfig.LogFilter.Filter);
                    factory.AddProvider(loggerProvider);
                });

            string rootPath = Path.Combine(Environment.CurrentDirectory, "ScriptHostTests");
            if (!Directory.Exists(rootPath))
            {
                Directory.CreateDirectory(rootPath);
            }

            File.WriteAllText(Path.Combine(rootPath, "host.json"), @"{<unparseable>}");

            var config = new ScriptHostConfiguration()
            {
                RootScriptPath = rootPath,
                LoggerFactoryBuilder = loggerFactoryHookMock.Object
            };
            config.HostConfig.HostId = ID;
            config.TraceWriter = new TestTraceWriter(TraceLevel.Info);

            var scriptHostFactory = new TestScriptHostFactory();
            var eventManagerMock = new Mock<IScriptEventManager>();
            var hostManager = new ScriptHostManager(config, _settingsManager, scriptHostFactory, eventManagerMock.Object);
            Task taskIgnore = Task.Run(() => hostManager.RunAndBlock());

            await TestHelpers.Await(() => hostManager.State == ScriptHostState.Error, 3000);

            Assert.Equal(ScriptHostState.Error, hostManager.State);
            hostManager.Stop();

            var ex = hostManager.LastError;
            Assert.True(ex is FormatException);
            Assert.Equal("Unable to parse host.json file.", ex.Message);

            var logger = loggerProvider.CreatedLoggers.Last();
            Assert.Equal(3, logger.LogMessages.Count);
            Assert.StartsWith("A ScriptHost error has occurred", logger.LogMessages[1].FormattedMessage);
            Assert.Equal("Unable to parse host.json file.", logger.LogMessages[1].Exception.Message);
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

            var testTraceWriter = new TestTraceWriter(TraceLevel.Verbose);
            var config = new ScriptHostConfiguration
            {
                RootScriptPath = functionDir,
                RootLogPath = logDir,
                FileLoggingMode = FileLoggingMode.Always,
                TraceWriter = testTraceWriter
            };

            // configure the monitor so it will fail within a couple seconds
            config.HostHealthMonitor.HealthCheckInterval = TimeSpan.FromMilliseconds(100);
            config.HostHealthMonitor.HealthCheckWindow = TimeSpan.FromSeconds(1);
            config.HostHealthMonitor.HealthCheckThreshold = 5;

            var environmentMock = new Mock<IScriptHostEnvironment>(MockBehavior.Strict);
            environmentMock.Setup(p => p.Shutdown());

            var mockSettings = new Mock<ScriptSettingsManager>();
            mockSettings.Setup(p => p.IsAzureEnvironment).Returns(true);

            var eventManagerMock = new Mock<IScriptEventManager>();
            var hostHealthConfig = new HostHealthMonitorConfiguration();
            var mockHostPerformanceManager = new Mock<HostPerformanceManager>(mockSettings.Object, hostHealthConfig);

            bool underHighLoad = false;
            mockHostPerformanceManager.Setup(p => p.IsUnderHighLoad(It.IsAny<Collection<string>>(), It.IsAny<TraceWriter>()))
                .Callback<Collection<string>, TraceWriter>((c, t) =>
                {
                    c.Add("Connections");
                })
                .Returns(() => underHighLoad);

            var hostManager = new ScriptHostManager(config, mockSettings.Object, new ScriptHostFactory(), eventManagerMock.Object, environmentMock.Object, mockHostPerformanceManager.Object);
            Assert.True(hostManager.ShouldMonitorHostHealth);
            Task runTask = Task.Run(() => hostManager.RunAndBlock());
            await TestHelpers.Await(() => hostManager.State == ScriptHostState.Running);

            // now that host is running make host unhealthy and wait
            // for host shutdown
            underHighLoad = true;

            await TestHelpers.Await(() => hostManager.State == ScriptHostState.Error);

            Assert.Equal(ScriptHostState.Error, hostManager.State);
            environmentMock.Verify(p => p.Shutdown(), Times.Once);

            var traces = testTraceWriter.GetTraces();

            // we expect a few restart iterations
            var thresholdErrors = traces.Where(p => p.Exception is InvalidOperationException && p.Exception.Message == "Host thresholds exceeded: [Connections]. For more information, see https://aka.ms/functions-thresholds.");
            Assert.True(thresholdErrors.Count() > 1);

            var log = traces.Last();
            Assert.True(traces.Count(p => p.Message == "Host is unhealthy. Initiating a restart." && p.Level == TraceLevel.Error) > 0);
            Assert.Equal("Host unhealthy count exceeds the threshold of 5 for time window 00:00:01. Initiating shutdown.", log.Message);
            Assert.Equal(TraceLevel.Error, log.Level);
        }

        [Fact]
        public async Task RunAndBlock_SetsLastError_WhenExceptionIsThrown()
        {
            ScriptHostConfiguration config = new ScriptHostConfiguration()
            {
                RootScriptPath = @"TestScripts\Empty",
                TraceWriter = new TestTraceWriter(TraceLevel.Info)
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
                RootScriptPath = Environment.CurrentDirectory,
                TraceWriter = new TestTraceWriter(TraceLevel.Verbose)
            };

            var mockSettings = new Mock<ScriptSettingsManager>(MockBehavior.Strict);
            var eventManager = new Mock<IScriptEventManager>();
            var hostMock = new Mock<ScriptHost>(new NullScriptHostEnvironment(), eventManager.Object, config, null, null);
            var factoryMock = new Mock<IScriptHostFactory>();
            factoryMock.Setup(f => f.Create(It.IsAny<IScriptHostEnvironment>(), It.IsAny<IScriptEventManager>(), mockSettings.Object, It.IsAny<ScriptHostConfiguration>()))
                .Returns(hostMock.Object);

            var hostHealthConfig = new HostHealthMonitorConfiguration();
            var mockHostPerformanceManager = new Mock<HostPerformanceManager>(mockSettings.Object, hostHealthConfig);
            var target = new Mock<ScriptHostManager>(config, mockSettings.Object, factoryMock.Object, eventManager.Object, new NullScriptHostEnvironment(), mockHostPerformanceManager.Object);

            Collection<string> exceededCounters = new Collection<string>();
            bool isUnderHighLoad = false;
            mockHostPerformanceManager.Setup(p => p.IsUnderHighLoad(It.IsAny<Collection<string>>(), It.IsAny<TraceWriter>()))
                .Callback<Collection<string>, TraceWriter>((c, t) =>
                {
                    foreach (var counter in exceededCounters)
                    {
                        c.Add(counter);
                    }
                })
                .Returns(() => isUnderHighLoad);

            bool isAzureEnvironment = false;
            mockSettings.Setup(p => p.IsAzureEnvironment).Returns(() => isAzureEnvironment);
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

            ScriptHostConfiguration config = new ScriptHostConfiguration()
            {
                RootScriptPath = functionDir,
                RootLogPath = logDir,
                FileLoggingMode = FileLoggingMode.Always,
                TraceWriter = new TestTraceWriter(TraceLevel.Info)
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

            await Task.Delay(FileTraceWriter.LogFlushIntervalMs);

            string hostLogFilePath = Directory.EnumerateFiles(Path.Combine(logDir, "Host")).Single();
            string hostLogs = File.ReadAllText(hostLogFilePath);

            Assert.Contains("Generating 0 job function(s)", hostLogs);
            Assert.Contains("No job functions found.", hostLogs);
            Assert.Contains("Job host started", hostLogs);
            Assert.Contains("Job host stopped", hostLogs);
        }

        [Fact]
        public void Restart_CreatesNew_FunctionTraceWriter()
        {
            string functionDir = @"TestScripts\CSharp";
            var traceWriter = new TestTraceWriter(System.Diagnostics.TraceLevel.Info);
            ScriptHostConfiguration config = new ScriptHostConfiguration
            {
                RootScriptPath = functionDir,
                FileLoggingMode = FileLoggingMode.Always,
                TraceWriter = traceWriter
            };

            string hostJsonPath = Path.Combine(functionDir, ScriptConstants.HostMetadataFileName);
            string originalHostJson = File.ReadAllText(hostJsonPath);

            // Only load two functions to start:
            JObject hostConfig = new JObject
            {
                { "id", "123456" },
                { "functions", new JArray("ManualTrigger", "Scenarios") }
            };
            File.WriteAllText(hostJsonPath, hostConfig.ToString());

            CancellationTokenSource cts = new CancellationTokenSource();
            ExceptionDispatchInfo exception = null;

            try
            {
                using (var manager = new ScriptHostManager(config))
                {
                    // Background task to run while the main thread is pumping events at RunAndBlock().
                    Thread t = new Thread(_ =>
                    {
                        try
                        {
                            // don't start until the manager is running
                            TestHelpers.Await(() => manager.State == ScriptHostState.Running).Wait();

                            var firstFileWriters = GetRemovableTraceWriters(manager.Instance);
                            Assert.Equal(2, firstFileWriters.Count());

                            // update the host.json to only have one function
                            hostConfig["functions"] = new JArray("ManualTrigger");
                            traceWriter.ClearTraces();
                            File.WriteAllText(hostJsonPath, hostConfig.ToString());
                            TestHelpers.Await(() => traceWriter.GetTraces().Select(p => p.Message).Contains("Job host started")).Wait();
                            TestHelpers.Await(() => manager.State == ScriptHostState.Running).Wait();

                            var secondFileWriters = GetRemovableTraceWriters(manager.Instance);
                            Assert.Equal(1, secondFileWriters.Count());

                            // make sure we have a new instance of the ManualTrigger writer and that it does
                            // not throw an ObjectDisposedException when we use it
                            Assert.DoesNotContain(secondFileWriters.Single(), firstFileWriters);
                            secondFileWriters.Single().Info("test");

                            // add back the other function -- make sure the writer is not disposed
                            hostConfig["functions"] = new JArray("ManualTrigger", "Scenarios");
                            traceWriter.ClearTraces();
                            File.WriteAllText(hostJsonPath, hostConfig.ToString());
                            TestHelpers.Await(() => traceWriter.GetTraces().Select(p => p.Message).Contains("Job host started")).Wait();
                            TestHelpers.Await(() => manager.State == ScriptHostState.Running).Wait();

                            var thirdFileWriters = GetRemovableTraceWriters(manager.Instance);
                            Assert.Equal(2, thirdFileWriters.Count());

                            // make sure these are all new and that they also do not throw
                            var previousWriters = firstFileWriters.Concat(secondFileWriters);
                            Assert.DoesNotContain(thirdFileWriters.First(), previousWriters);
                            Assert.DoesNotContain(thirdFileWriters.Last(), previousWriters);
                            thirdFileWriters.First().Info("test");
                            thirdFileWriters.Last().Info("test");
                        }
                        catch (Exception ex)
                        {
                            exception = ExceptionDispatchInfo.Capture(ex);
                        }
                        finally
                        {
                            cts.Cancel();
                        }
                    });
                    t.Start();
                    manager.RunAndBlock(cts.Token);
                    t.Join();
                }

                Assert.True(exception == null, exception?.SourceException?.ToString());
            }
            finally
            {
                File.WriteAllText(hostJsonPath, originalHostJson);
            }
        }

        private static IEnumerable<RemovableTraceWriter> GetRemovableTraceWriters(ScriptHost host)
        {
            List<RemovableTraceWriter> removableTraceWriters = new List<RemovableTraceWriter>();

            foreach (var function in host.Functions)
            {
                var invokerBase = function.Invoker as FunctionInvokerBase;
                if (invokerBase == null)
                {
                    continue;
                }

                RemovableTraceWriter instance = null;
                if (invokerBase.FileTraceWriter is ConditionalTraceWriter conditional)
                {
                    instance = conditional.InnerWriter as RemovableTraceWriter;
                }
                else
                {
                    instance = invokerBase.FileTraceWriter as RemovableTraceWriter;
                }

                if (instance != null)
                {
                    removableTraceWriters.Add(instance);
                }
            }

            return removableTraceWriters;
        }

        // Update the manifest for the timer function
        // - this will cause a file touch which cause ScriptHostManager to notice and update
        // - set to a new output location so that we can ensure we're getting new changes.
        private static CloudBlockBlob UpdateOutputName(string prev, string hint, EndToEndTestFixture fixture)
        {
            string name = hint;

            string manifestPath = Path.Combine(Environment.CurrentDirectory, @"TestScripts\Node\TimerTrigger\function.json");
            string content = File.ReadAllText(manifestPath);
            content = content.Replace(prev, name);
            File.WriteAllText(manifestPath, content);

            var blob = fixture.TestOutputContainer.GetBlockBlobReference(name);
            blob.DeleteIfExists();
            return blob;
        }

        private class TestScriptHostFactory : IScriptHostFactory
        {
            public bool Throw { get; set; }

            public ScriptHost Create(IScriptHostEnvironment environment, IScriptEventManager eventManager, ScriptSettingsManager settingsManager, ScriptHostConfiguration config)
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

                return new ScriptHost(environment, eventManager, config, settingsManager);
            }
        }
    }
}