// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.WindowsAzure.Storage.Blob;
using Moq;
using Moq.Protected;
using Newtonsoft.Json.Linq;
using Xunit;
using static Microsoft.Azure.WebJobs.Script.FunctionTraceWriterFactory;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class ScriptHostManagerTests
    {
        private readonly ScriptSettingsManager _settingsManager;

        public ScriptHostManagerTests()
        {
            _settingsManager = ScriptSettingsManager.Instance;
        }

        // TODO: FACAVAL NODE
        //// Update a script file (the function.json) to force the ScriptHost to re-index and pick up new changes.
        //// Test with timers:
        [Fact]
        public async Task UpdateFileAndRestart()
        {
            CancellationTokenSource cts = new CancellationTokenSource();
            var fixture = new NodeEndToEndTests.TestFixture();
            var blob1 = await UpdateOutputName("testblob", "first", fixture);

            await fixture.Host.StopAsync();
            var config = fixture.Host.ScriptConfig;

            ExceptionDispatchInfo exception = null;
            using (var eventManager = new ScriptEventManager())
            using (var manager = new ScriptHostManager(config, eventManager))
            {
                // Background task to run while the main thread is pumping events at RunAndBlock().
                Thread t = new Thread(_ =>
                {
                    // don't start until the manager is running
                    TestHelpers.Await(() => manager.State == ScriptHostState.Running).Wait();

                    try
                    {
                        // Wait for initial execution.
                        TestHelpers.Await(async () =>
                        {
                            bool exists = await blob1.ExistsAsync();
                            return exists;
                        }, timeout: 10 * 1000).Wait();

                        // This changes the bindings so that we now write to blob2
                        var blob2 = UpdateOutputName("first", "testblob", fixture).Result;

                        // wait for newly executed
                        TestHelpers.Await(async () =>
                        {
                            bool exists = await blob2.ExistsAsync();
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
                            UpdateOutputName("first", "testblob", fixture).Wait();
                        }
                        catch
                        {
                        }
                    }

                    cts.Cancel();
                });
                t.Start();

                manager.RunAndBlock(cts.Token);

                t.Join();

                Assert.True(exception == null, exception?.SourceException?.ToString());
            }
        }

        [Fact]
        public async Task RenameFunctionAndRestart()
        {
            var oldDirectory = Path.Combine(Directory.GetCurrentDirectory(), "TestScripts/Node/TimerTrigger");
            var newDirectory = Path.Combine(Directory.GetCurrentDirectory(), "TestScripts/Node/MovedTrigger");

            CancellationTokenSource cts = new CancellationTokenSource();
            var fixture = new NodeEndToEndTests.TestFixture();
            await fixture.Host.StopAsync();
            var config = fixture.Host.ScriptConfig;

            var blob = fixture.TestOutputContainer.GetBlockBlobReference("testblob");

            ExceptionDispatchInfo exception = null;
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
                    // don't start until the manager is running
                    TestHelpers.Await(() => manager.State == ScriptHostState.Running).Wait();

                    try
                    {
                        // Wait for initial execution.
                        TestHelpers.Await(async () =>
                        {
                            bool exists = await blob.ExistsAsync();
                            return exists;
                        }, timeout: 10 * 1000).Wait();

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

                        blob.DeleteIfExistsAsync();

                        // wait for newly executed
                        TestHelpers.Await(async () =>
                        {
                            bool exists = await blob.ExistsAsync();
                            return exists;
                        }, timeout: 30 * 1000).Wait();

                        using (var stream = new MemoryStream())
                        {
                            blob.DownloadToStreamAsync(stream).Wait();
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
                        catch
                        {
                        }
                    }

                    cts.Cancel();
                });
                t.Start();

                manager.RunAndBlock(cts.Token);

                t.Join();

                Assert.True(exception == null, exception?.SourceException?.ToString());
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
            factoryMock.Setup(f => f.Create(It.IsAny<IScriptHostEnvironment>(), It.IsAny<IScriptEventManager>(), _settingsManager, It.IsAny<ScriptHostConfiguration>(), It.IsAny<ILoggerFactoryBuilder>()))
                .Returns(hostMock.Object);

            var target = new Mock<ScriptHostManager>(config, _settingsManager, factoryMock.Object, eventManager.Object, new NullScriptHostEnvironment(), new DefaultLoggerFactoryBuilder());
            target.Protected().Setup("OnHostStarted")
                .Throws(new Exception());

            hostMock.Protected().Setup("Dispose", true)
                .Callback(() => target.Object.Stop());

            Task.Run(() => target.Object.RunAndBlock()).Wait(50000);

            hostMock.Protected().Verify("Dispose", Times.Once(), true);
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

            Task runTask = Task.Run(() => hostManager.RunAndBlock());

            await TestHelpers.Await(() => hostManager.State == ScriptHostState.Running, timeout: 10000);

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
                            traceWriter.Traces.Clear();
                            File.WriteAllText(hostJsonPath, hostConfig.ToString());
                            TestHelpers.Await(() => traceWriter.Traces.Select(p => p.Message).Contains("Job host started")).Wait();
                            TestHelpers.Await(() => manager.State == ScriptHostState.Running).Wait();

                            var secondFileWriters = GetRemovableTraceWriters(manager.Instance);
                            Assert.Equal(1, secondFileWriters.Count());

                            // make sure we have a new instance of the ManualTrigger writer and that it does
                            // not throw an ObjectDisposedException when we use it
                            Assert.DoesNotContain(secondFileWriters.Single(), firstFileWriters);
                            secondFileWriters.Single().Info("test");

                            // add back the other function -- make sure the writer is not disposed
                            hostConfig["functions"] = new JArray("ManualTrigger", "Scenarios");
                            traceWriter.Traces.Clear();
                            File.WriteAllText(hostJsonPath, hostConfig.ToString());
                            TestHelpers.Await(() => traceWriter.Traces.Select(p => p.Message).Contains("Job host started")).Wait();
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
        private static async Task<CloudBlockBlob> UpdateOutputName(string prev, string hint, EndToEndTestFixture fixture)
        {
            string name = hint;

            string manifestPath = Path.Combine(Environment.CurrentDirectory, @"TestScripts\Node\TimerTrigger\function.json");
            string content = File.ReadAllText(manifestPath);
            content = content.Replace(prev, name);
            File.WriteAllText(manifestPath, content);

            var blob = fixture.TestOutputContainer.GetBlockBlobReference(name);
            await blob.DeleteIfExistsAsync();
            return blob;
        }

        private class TestScriptHostFactory : IScriptHostFactory
        {
            public bool Throw { get; set; }

            public ScriptHost Create(IScriptHostEnvironment environment, IScriptEventManager eventManager, ScriptSettingsManager settingsManager, ScriptHostConfiguration config, ILoggerFactoryBuilder loggerFactoryBuilder)
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

                return ScriptHost.Create(environment, eventManager, config, settingsManager, loggerFactoryBuilder);
            }
        }
    }
}