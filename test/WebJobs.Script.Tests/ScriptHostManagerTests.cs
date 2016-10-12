// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.WindowsAzure.Storage.Blob;
using Moq;
using Moq.Protected;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    [Trait("Category", "E2E")]
    public class ScriptHostManagerTests
    {
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

            using (var manager = new ScriptHostManager(config))
            {
                // Background task to run while the main thread is pumping events at RunAndBlock(). 
                Thread t = new Thread(_ =>
                   {
                       // don't start until the manager is running
                       TestHelpers.Await(() => manager.IsRunning).Wait();

                       try
                       {
                           // Wait for initial execution.
                           TestHelpers.Await(() =>
                           {
                               bool exists = blob1.Exists();
                               return exists;
                           }, timeout: 10 * 1000).Wait();

                           // This changes the bindings so that we now write to blob2
                           var blob2 = UpdateOutputName("first", "second", fixture);

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

            var hostMock = new Mock<ScriptHost>(config);
            var factoryMock = new Mock<IScriptHostFactory>();
            factoryMock.Setup(f => f.Create(_settingsManager, It.IsAny<ScriptHostConfiguration>()))
                .Returns(hostMock.Object);

            var target = new Mock<ScriptHostManager>(config, _settingsManager, factoryMock.Object);
            target.Protected().Setup("OnHostStarted")
                .Throws(new Exception());

            hostMock.Protected().Setup("Dispose", true)
                .Callback(() => target.Object.Stop());

            Task.Run(() => target.Object.RunAndBlock()).Wait(5000);

            hostMock.Protected().Verify("Dispose", Times.Once(), true);
        }

        [Fact]
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
            var hostManager = new ScriptHostManager(config, _settingsManager, scriptHostFactory);
            Task taskIgnore = Task.Run(() => hostManager.RunAndBlock());

            // we expect a host exception immediately
            await Task.Delay(2000);

            Assert.False(hostManager.IsRunning);
            Assert.NotNull(hostManager.LastError);
            Assert.Equal("Kaboom!", hostManager.LastError.Message);

            // now verify that if no error is thrown on the next iteration
            // the cached error is cleared
            scriptHostFactory.Throw = false;
            await TestHelpers.Await(() =>
            {
                return hostManager.IsRunning;
            });

            Assert.Null(hostManager.LastError);
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
            ScriptHostManager hostManager = new ScriptHostManager(config);

            Task runTask = Task.Run(() => hostManager.RunAndBlock());

            await TestHelpers.Await(() => hostManager.IsRunning, timeout: 10000);

            hostManager.Stop();
            Assert.False(hostManager.IsRunning);

            await Task.Delay(FileTraceWriter.LogFlushIntervalMs);

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

            public ScriptHost Create(ScriptSettingsManager settingsManager, ScriptHostConfiguration config)
            {
                if (Throw)
                {
                    throw new Exception("Kaboom!");
                }

                var mockMetricsLogger = new Mock<IMetricsLogger>(MockBehavior.Strict);
                config.HostConfig.AddService<IMetricsLogger>(mockMetricsLogger.Object);
                mockMetricsLogger.Setup(p => p.BeginEvent(It.IsAny<string>())).Returns(new object());
                mockMetricsLogger.Setup(p => p.EndEvent(It.IsAny<object>()));
                mockMetricsLogger.Setup(p => p.LogEvent(It.IsAny<string>()));
                mockMetricsLogger.Setup(p => p.LogEvent(It.IsAny<MetricEvent>()));

                return ScriptHost.Create(settingsManager, config);
            }
        }
    }
}