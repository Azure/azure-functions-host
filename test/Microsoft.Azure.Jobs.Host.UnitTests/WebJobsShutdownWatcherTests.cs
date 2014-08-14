// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using Microsoft.Azure.Jobs.Host.TestCommon;
using Xunit;

namespace Microsoft.Azure.Jobs.Host.UnitTests
{
    // Unit test the static parameter bindings. This primarily tests the indexer.
    public class WebJobsShutdownWatcherTests
    {
        [Fact]
        public void InvalidPath()
        {
            using (new WebJobsShutdownContext(@"C:\This\path\should\not\exist"))
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
            using (WebJobsShutdownContext shutdownContext = new WebJobsShutdownContext())
            using (var watcher = new WebJobsShutdownWatcher())
            {
                var token = watcher.Token;
                Assert.True(!token.IsCancellationRequested);

                // Write the file
                shutdownContext.NotifyShutdown();

                // Token should be signaled very soon 
                Assert.True(token.WaitHandle.WaitOne(500));
            }
        }

        [Fact]
        public void NotSignaledAfterDisposed()
        {
            using (WebJobsShutdownContext shutdownContext = new WebJobsShutdownContext())
            {

                CancellationToken token;
                using (var watcher = new WebJobsShutdownWatcher())
                {
                    token = watcher.Token;
                    Assert.True(!token.IsCancellationRequested);
                }
                // Write the file
                shutdownContext.NotifyShutdown();

                Assert.True(!token.IsCancellationRequested);
            }
        }

        [Fact]
        public void None()
        {
            // Env var not set
            using (new WebJobsShutdownContext(null))
            using (var watcher = new WebJobsShutdownWatcher())
            {
                var token = watcher.Token;

                // When no env set, then token can never be cancelled. 
                Assert.True(!token.CanBeCanceled);
                Assert.True(!token.IsCancellationRequested);
            }
        }
    }
}
