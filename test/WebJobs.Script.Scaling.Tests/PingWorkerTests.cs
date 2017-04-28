// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Scaling.Tests
{
    public class PingWorkerTests
    {
        [Fact]
        public async Task ValidWorkerTests()
        {
            var activityId = Guid.NewGuid().ToString();
            var workerInfo = new MockWorkerInfo();

            // Test
            using (var scaleManager = new MockScaleManager(MockBehavior.Strict))
            {
                // Setup
                scaleManager.MockScaleHandler.Setup(h => h.PingWorker(activityId, workerInfo))
                    .Returns(Task.FromResult(true));
                scaleManager.MockWorkerTable.Setup(t => t.AddOrUpdate(workerInfo))
                    .Returns(Task.CompletedTask);
                scaleManager.MockScaleTracer.Setup(t => t.TraceUpdateWorker(activityId, workerInfo, It.Is<string>(s => s.Contains("updated"))));

                // test
                await scaleManager.MockPingWorker(activityId, workerInfo);

                // assert
                scaleManager.VerifyAll();
            }
        }

        [Fact]
        public async Task InvalidWorkerTests()
        {
            var activityId = Guid.NewGuid().ToString();
            var workerInfo = new MockWorkerInfo();

            // Test
            using (var scaleManager = new MockScaleManager(MockBehavior.Strict))
            {
                // Setup
                scaleManager.MockScaleHandler.Setup(h => h.PingWorker(activityId, workerInfo))
                    .Returns(Task.FromResult(false));
                scaleManager.MockWorkerTable.Setup(t => t.Delete(workerInfo))
                    .Returns(Task.CompletedTask);
                scaleManager.MockScaleTracer.Setup(t => t.TraceWarning(activityId, workerInfo, It.Is<string>(s => s.Contains("not belong"))));
                scaleManager.MockScaleTracer.Setup(t => t.TraceRemoveWorker(activityId, workerInfo, It.Is<string>(s => s.Contains("removed"))));

                // test
                var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await scaleManager.MockPingWorker(activityId, workerInfo));

                // assert
                Assert.Contains("not belong", exception.Message);
                scaleManager.VerifyAll();
            }
        }

        [Fact]
        public async Task PingWorkerIntervalTests()
        {
            var activityId = Guid.NewGuid().ToString();
            var workerInfo = new MockWorkerInfo();
            var settings = new ScaleSettings
            {
                WorkerPingInterval = TimeSpan.FromMilliseconds(500)
            };

            using (var scaleManager = new MockScaleManager(MockBehavior.Strict, settings))
            {
                // Setup
                scaleManager.MockScaleHandler.Setup(h => h.PingWorker(activityId, workerInfo))
                    .Returns(Task.FromResult(true));
                scaleManager.MockWorkerTable.Setup(t => t.AddOrUpdate(workerInfo))
                    .Returns(Task.CompletedTask);
                scaleManager.MockScaleTracer.Setup(t => t.TraceUpdateWorker(activityId, workerInfo, It.Is<string>(s => s.Contains("updated"))));

                var loop = 10;
                for (int i = 0; i < loop; ++i)
                {
                    // test
                    await scaleManager.MockPingWorker(activityId, workerInfo);

                    await Task.Delay(100);
                }

                // assert
                scaleManager.VerifyAll();
                scaleManager.MockScaleHandler.Verify(h => h.PingWorker(activityId, workerInfo), Times.Between(2, loop - 1, Range.Inclusive));
                scaleManager.MockWorkerTable.Verify(t => t.AddOrUpdate(workerInfo), Times.Exactly(loop));
                scaleManager.MockScaleTracer.Verify(t => t.TraceUpdateWorker(activityId, workerInfo, It.Is<string>(s => s.Contains("updated"))), Times.Exactly(loop));
            }
        }
    }
}