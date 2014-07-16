// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using Xunit;

namespace Microsoft.Azure.Jobs.Host.UnitTests
{
    // Unit test the static parameter bindings. This primarily tests the indexer.
    public class WebJobsShutdownWatcherTests : IDisposable
    {
        [Fact]
        public void InvalidPath()
        {
            WriteEnvVar(@"C:\This\path\should\not\exist");

            using (var watcher = new WebJobsShutdownWatcher())
            {
                var token = watcher.Token;

                // When no env set, then token can never be cancelled. 
                Assert.True(!token.CanBeCanceled);
                Assert.True(!token.IsCancellationRequested);
            }
        }

        [Fact]
        public void Signaled()
        {
            string path = WriteEnvVar();

            using (var watcher = new WebJobsShutdownWatcher())
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
            using (var watcher = new WebJobsShutdownWatcher())
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
            RemoveEnvVar();

            using (var watcher = new WebJobsShutdownWatcher())
            {
                var token = watcher.Token;

                // When no env set, then token can never be cancelled. 
                Assert.True(!token.CanBeCanceled);
                Assert.True(!token.IsCancellationRequested);
            }
        }

        public void Dispose()
        {
            RemoveEnvVar();
        }

        private static string WriteEnvVar()
        {
            return WriteEnvVar(Path.GetTempFileName());
        }

        private static string WriteEnvVar(string path)
        {
            Environment.SetEnvironmentVariable("WEBJOBS_SHUTDOWN_FILE", path);
            return path;
        }

        private static void RemoveEnvVar()
        {
            Environment.SetEnvironmentVariable("WEBJOBS_SHUTDOWN_FILE", null);
        }
    }
}
