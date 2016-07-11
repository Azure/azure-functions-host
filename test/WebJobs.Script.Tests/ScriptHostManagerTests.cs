// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        // Update a script file (the function.json) to force the ScriptHost to re-index and pick up new changes. 
        // Test with timers: 
        [Fact]
        public async Task UpdateFileAndRestart()
        {
            Random r = new Random();

            CancellationTokenSource cts = new CancellationTokenSource();

            var fixture = new NodeEndToEndTests.TestFixture();
            var blob1 = UpdateOutputName("testblob", "first", fixture);

            await fixture.Host.StopAsync();
            var config = fixture.Host.ScriptConfig;            

            using (var manager = new ScriptHostManager(config))
            {
                // Background task to run while the main thread is pumping events at RunAndBlock(). 
                Thread t = new Thread(_ =>
                   {
                       // Wait for initial execution.
                       TestHelpers.Await(() =>
                       {
                           return blob1.Exists();
                       }, timeout: 10 * 1000).Wait();

                       // This changes the bindings so that we now write to blob2
                       var blob2 = UpdateOutputName("first", "second", fixture);

                       // wait for newly executed
                       TestHelpers.Await(() =>
                       {
                           return blob2.Exists();
                       }, timeout: 10 * 1000).Wait();

                       manager.Stop();
                   });
                t.Start();                           

                manager.RunAndBlock(cts.Token);

                t.Join();
            }
        }

        [Fact]
        public void RunAndBlock_DisposesOfHost_WhenExceptionIsThrown()
        {
            ScriptHostConfiguration config = new ScriptHostConfiguration()
            {
                RootScriptPath = Environment.CurrentDirectory
            };

            var hostMock = new Mock<TestScriptHost>(config);
            var factoryMock = new Mock<IScriptHostFactory>();
            factoryMock.Setup(f => f.Create(It.IsAny<ScriptHostConfiguration>()))
                .Returns(hostMock.Object);

            var target = new Mock<ScriptHostManager>(config, factoryMock.Object);
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
                RootScriptPath = Environment.CurrentDirectory
            };

            var exception = new Exception("Kaboom!");
            var hostMock = new Mock<TestScriptHost>(config);
            var factoryMock = new Mock<IScriptHostFactory>();
            factoryMock.Setup(f => f.Create(It.IsAny<ScriptHostConfiguration>()))
                .Returns(() =>
                {
                    if (exception != null)
                    {
                        throw exception;
                    }
                    return hostMock.Object;
                });

            var target = new Mock<ScriptHostManager>(config, factoryMock.Object);
            Task taskIgnore = Task.Run(() => target.Object.RunAndBlock());

            // we expect a host exception immediately
            await Task.Delay(2000);

            Assert.False(target.Object.IsRunning);
            Assert.Same(exception, target.Object.LastError);

            // now verify that if no error is thrown on the next iteration
            // the cached error is cleared
            exception = null;
            await TestHelpers.Await(() =>
            {
                return target.Object.IsRunning;
            });

            Assert.Null(target.Object.LastError);
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
                FileLoggingEnabled = true
            };
            ScriptHostManager hostManager = new ScriptHostManager(config);

            Task runTask = Task.Run(() => hostManager.RunAndBlock());

            await TestHelpers.Await(() => hostManager.IsRunning, timeout: 10000);

            hostManager.Stop();
            Assert.False(hostManager.IsRunning);

            string hostLogFilePath = Directory.EnumerateFiles(Path.Combine(logDir, "Host")).Single();
            string hostLogs = File.ReadAllText(hostLogFilePath);

            Assert.True(hostLogs.Contains("Generating 0 job function(s)"));
            Assert.True(hostLogs.Contains("No job functions found."));
            Assert.True(hostLogs.Contains("Job host started"));
            Assert.True(hostLogs.Contains("Job host stopped"));
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

        public class TestScriptHost : ScriptHost
        {
            public TestScriptHost(ScriptHostConfiguration scriptConfig) : base(scriptConfig)
            {
            }
        }
    }
}