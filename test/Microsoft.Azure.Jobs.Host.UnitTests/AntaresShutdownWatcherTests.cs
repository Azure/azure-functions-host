using System;
using System.IO;
using System.Threading;
using Microsoft.Azure.Jobs.Host;
using Microsoft.Azure.Jobs.Host.TestCommon;
using Xunit;

namespace Microsoft.Azure.Jobs.Host.UnitTests
{
    // Unit test the static parameter bindings. This primarily tests the indexer.
    public class AntaresShutdownWatcherTests : IDisposable
    {
        [Fact]
        public void Signaled()
        {
            string path = WriteEnvVar();

            using (var watcher = new WebjobsShutdownWatcher())
            {
                var token = watcher.Token;
                Assert.True(!token.IsCancellationRequested);

                // Write the file
                File.WriteAllText(path, "x");

                // Token should be signaled very soon 
                Assert.True(token.WaitHandle.WaitOne(500));
            }
        }

        [Fact]
        public void NotSignaledAfterDisposed()
        {
            string path = WriteEnvVar();

            CancellationToken token;
            using (var watcher = new WebjobsShutdownWatcher())
            {
                token = watcher.Token;
                Assert.True(!token.IsCancellationRequested);
            }
            // Write the file
            File.WriteAllText(path, "x");

            Assert.True(!token.IsCancellationRequested);

        }

        [Fact]
        public void None()
        {
            // Env var not set
            Assert.Null(Environment.GetEnvironmentVariable("WEBJOBS_SHUTDOWN_FILE"));

            using (var watcher = new WebjobsShutdownWatcher())
            {
                var token = watcher.Token;

                // When no env set, then token can never be cancelled. 
                Assert.True(!token.CanBeCanceled);
                Assert.True(!token.IsCancellationRequested);
            }
        }

        static string WriteEnvVar()
        {
            string path = Path.GetTempFileName();
            Environment.SetEnvironmentVariable("WEBJOBS_SHUTDOWN_FILE", path);
            return path;
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("WEBJOBS_SHUTDOWN_FILE", null);
        }
    }
}