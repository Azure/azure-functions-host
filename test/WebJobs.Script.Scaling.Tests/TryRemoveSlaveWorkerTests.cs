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
    public class TryRemoveSlaveWorkerTests
    {
        [Theory, MemberData(nameof(BasicData))]
        public async Task BasicTests(IWorkerInfo manager, IEnumerable<IWorkerInfo> workers, bool added, IWorkerInfo toRemove)
        {
            var activityId = Guid.NewGuid().ToString();
            var settings = new ScaleSettings
            {
                BusyWorkerLoadFactor = 80,
                FreeWorkerLoadFactor = 20
            };

            var mockManager = new Mock<MockScaleManager>(MockBehavior.Default, MockBehavior.Strict, settings) { CallBase = true };

            // Test
            using (var scaleManager = mockManager.Object)
            {
                // Setup
                if (toRemove != null)
                {
                    scaleManager.MockScaleTracer.Setup(t => t.TraceInformation(activityId, toRemove, It.Is<string>(s => s.Contains("remove slave worker"))));

                    mockManager.Setup(m => m.MockRequestAddWorker(activityId, Enumerable.Empty<IWorkerInfo>(), manager, true, false))
                        .Returns(Task.FromResult(added));

                    if (added)
                    {
                        mockManager.Setup(m => m.MockRequestRemoveWorker(activityId, manager, toRemove))
                            .Returns(Task.FromResult(true));
                    }
                }

                // test
                var actual = await scaleManager.MockTryRemoveSlaveWorker(activityId, workers, manager);

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
                var homeNormal = new MockWorkerInfo { StampName = "home-stamp", WorkerName = "1", LoadFactor = 50 };
                var homeBusy = new MockWorkerInfo { StampName = "home-stamp", WorkerName = "2", LoadFactor = 90 };
                var slaveNormal = new MockWorkerInfo { StampName = "slave-stamp", WorkerName = "3", LoadFactor = 50 };
                var slaveBusy = new MockWorkerInfo { StampName = "slave-stamp", WorkerName = "4", LoadFactor = 90 };

                yield return new object[] { homeNormal, new[] { homeNormal }, true, null };
                yield return new object[] { homeNormal, new[] { homeNormal, slaveBusy }, true, null };
                yield return new object[] { homeNormal, new[] { homeBusy, slaveNormal }, true, null };
                yield return new object[] { homeNormal, new[] { slaveBusy }, true, null };
                yield return new object[] { homeNormal, new[] { homeNormal, slaveNormal }, true, slaveNormal };
                yield return new object[] { homeNormal, new[] { homeNormal, slaveNormal }, false, slaveNormal };
                yield return new object[] { homeNormal, new[] { slaveNormal }, true, slaveNormal };
                yield return new object[] { homeNormal, new[] { slaveNormal }, false, slaveNormal };
            }
        }
    }
}