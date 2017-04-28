// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Scaling.Tests
{
    public class TryAddIfMaxBusyWorkerRatioTests
    {
        [Theory, MemberData("BasicData")]
        public async Task BasicTests(double maxBusyWorkerRatio, IWorkerInfo manager, IEnumerable<IWorkerInfo> workers, bool expected)
        {
            var activityId = Guid.NewGuid().ToString();
            var settings = new ScaleSettings
            {
                MaxBusyWorkerRatio = maxBusyWorkerRatio,
                BusyWorkerLoadFactor = 80
            };

            var mockManager = new Mock<MockScaleManager>(MockBehavior.Default, MockBehavior.Strict, settings) { CallBase = true };

            // Test
            using (var scaleManager = mockManager.Object)
            {
                // Setup
                if (expected)
                {
                    scaleManager.MockScaleTracer.Setup(t => t.TraceInformation(activityId, manager, It.Is<string>(s => s.Contains("exceeds maximum busy worker ratio"))));

                    mockManager.Setup(m => m.MockRequestAddWorker(activityId, workers, manager, false))
                        .Returns(Task.FromResult(true));
                }

                // test
                var actual = await scaleManager.MockTryAddIfMaxBusyWorkerRatio(activityId, workers, manager);

                // assert
                mockManager.VerifyAll();
                scaleManager.VerifyAll();
                Assert.Equal(expected, actual);
            }
        }

        public static IEnumerable<object[]> BasicData
        {
            get
            {
                var homeFree = new MockWorkerInfo { StampName = "home-stamp", WorkerName = "1", LoadFactor = 20 };
                var homeBusy = new MockWorkerInfo { StampName = "home-stamp", WorkerName = "2", LoadFactor = 80 };
                var slaveFree = new MockWorkerInfo { StampName = "slave-stamp", WorkerName = "3", LoadFactor = 20 };
                var slaveBusy = new MockWorkerInfo { StampName = "slave-stamp", WorkerName = "4", LoadFactor = 80 };

                yield return new object[] { 0.5, homeFree, new[] { homeFree }, false };
                yield return new object[] { 0.5, homeBusy, new[] { homeBusy }, true };
                yield return new object[] { 0.5, homeFree, new[] { homeFree, homeBusy }, false };
                yield return new object[] { 0.5, homeFree, new[] { homeFree, homeBusy, slaveBusy }, true };
                yield return new object[] { 0.5, homeFree, new[] { homeFree, homeBusy, slaveFree }, false };
                yield return new object[] { 0.5, homeFree, new[] { homeFree, homeBusy, slaveFree, slaveBusy }, false };
            }
        }
    }
}