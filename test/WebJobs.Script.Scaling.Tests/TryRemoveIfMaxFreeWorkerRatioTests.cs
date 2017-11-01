// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Scaling.Tests
{
    public class TryRemoveIfMaxFreeWorkerRatioTests
    {
        [Theory, MemberData(nameof(BasicData))]
        public async Task BasicTests(double maxFreeWorkerRatio, IWorkerInfo manager, IEnumerable<IWorkerInfo> workers, IWorkerInfo toRemove)
        {
            var activityId = Guid.NewGuid().ToString();
            var settings = new ScaleSettings
            {
                MaxFreeWorkerRatio = maxFreeWorkerRatio,
                FreeWorkerLoadFactor = 20
            };

            var mockManager = new Mock<MockScaleManager>(MockBehavior.Default, MockBehavior.Strict, settings) { CallBase = true };

            // Test
            using (var scaleManager = mockManager.Object)
            {
                // Setup
                if (toRemove != null)
                {
                    scaleManager.MockScaleTracer.Setup(t => t.TraceInformation(activityId, manager, It.Is<string>(s => s.Contains("exceeds maximum free worker ratio"))));

                    mockManager.Setup(m => m.MockRequestRemoveWorker(activityId, manager, toRemove))
                        .Returns(Task.FromResult(true));
                }

                // test
                var actual = await scaleManager.MockTryRemoveIfMaxFreeWorkerRatio(activityId, workers, manager);

                // assert
                mockManager.VerifyAll();
                scaleManager.VerifyAll();
                Assert.Equal(toRemove != null, actual);
            }
        }

        public static IEnumerable<object[]> BasicData
        {
            get
            {
                var homeFree = new MockWorkerInfo { StampName = "home-stamp", WorkerName = "1", LoadFactor = 10 };
                var anotherFree = new MockWorkerInfo { StampName = "home-stamp", WorkerName = "2", LoadFactor = 20 };
                var slaveFree = new MockWorkerInfo { StampName = "slave-stamp", WorkerName = "3", LoadFactor = 20 };
                var slaveNormal = new MockWorkerInfo { StampName = "slave-stamp", WorkerName = "4", LoadFactor = 50 };

                yield return new object[] { 0.5, homeFree, new[] { homeFree }, homeFree };
                yield return new object[] { 0.5, homeFree, new[] { homeFree, slaveFree }, slaveFree };
                yield return new object[] { 0.5, homeFree, new[] { homeFree, slaveNormal }, null };
                yield return new object[] { 0.5, homeFree, new[] { homeFree, slaveNormal, slaveFree }, slaveFree };
                yield return new object[] { 0.5, homeFree, new[] { homeFree, anotherFree, slaveNormal }, slaveNormal };
            }
        }
    }
}