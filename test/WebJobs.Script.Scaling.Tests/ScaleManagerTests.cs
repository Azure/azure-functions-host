// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Scaling.Tests
{
    public class ScaleManagerTests
    {
        [Fact]
        public void UpdateWorkerStatusTimerTests()
        {
            var worker = new MockWorkerInfo();
            var settings = new ScaleSettings
            {
                WorkerUpdateInterval = TimeSpan.FromMilliseconds(500)
            };

            var evt = new AutoResetEvent(false);
            var mockScaleManager = new Mock<MockScaleManager>(Mock.Of<IWorkerInfoProvider>(), Mock.Of<IWorkerTable>(), Mock.Of<IScaleHandler>(), Mock.Of<IScaleTracer>(), settings)
            {
                CallBase = true
            };

            // Setup
            mockScaleManager.Setup(m => m.MockProcessWorkItem(It.IsAny<string>()))
                .Callback((string id) => evt.Set())
                .Returns(Task.CompletedTask);

            using (var manager = mockScaleManager.Object)
            {
                // Assert
                Assert.False(evt.WaitOne(1000));

                // Test
                manager.Start();

                // Assert
                Assert.True(evt.WaitOne(1000));
            }
        }
    }
}