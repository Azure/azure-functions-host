// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Xunit;
using System;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Indexers
{
    public class FunctionNameTests
    {
        [Fact]
        public async Task Test()
        {
            var prog = new MyProg();
            var activator = new FakeActivator();
            activator.Add(prog);
            var logger = new MyLogger();
            var host = TestHelpers.NewJobHost<MyProg>(activator, logger);

            // Invoke with method Info
            var method = prog.GetType().GetMethod("Test");
            host.Call(method);
            prog.AssertValid();
            logger.AssertValid();

            // Invoke with new name. 
            await host.CallAsync(MyProg.NewName);
            prog.AssertValid();
            logger.AssertValid();

            // Invoke with original name fails 
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await host.CallAsync("Test"));
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await host.CallAsync("MyProg.Test"));
        }    

        [Fact]
        public void TestInvalidName()
        {
            var host = TestHelpers.NewJobHost<ProgInvalidName>();
            TestHelpers.AssertIndexingError(() => host.Call("Test"), "ProgInvalidName.Test", "'x y' is not a valid function name.");
        }

        public class ProgInvalidName
        {
            [NoAutomaticTrigger]
            [FunctionName("x y")] // illegal charecters
            public void Test()
            {
            }
        }

        public class MyLogger : IAsyncCollector<FunctionInstanceLogEntry>
        {
            public List<string> _items = new List<string>();

            public void AssertValid()
            {
                Assert.Equal(MyProg.NewName, _items[0]);
                _items.Clear();
            }

            public Task AddAsync(FunctionInstanceLogEntry item, CancellationToken cancellationToken = default(CancellationToken))
            {
                _items.Add(item.FunctionName);
                return Task.CompletedTask;
            }

            public Task FlushAsync(CancellationToken cancellationToken = default(CancellationToken))
            {
                return Task.CompletedTask;
            }
        }

        public class MyProg
        {
            public const string NewName = "otherName";
            public int _called;

            public void AssertValid()
            {
                Assert.Equal(1, _called);
                _called = 0;
            }

            [NoAutomaticTrigger]
            [FunctionName(NewName)]
            public void Test()
            {
                _called++;
            }
        }
    }
}
