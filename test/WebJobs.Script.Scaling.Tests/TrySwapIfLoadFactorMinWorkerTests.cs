// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Scaling.Tests
{
    public class TrySwapIfLoadFactorMinWorkerTests
    {
        [Theory, MemberData(nameof(BasicData))]
        public async Task BasicTests(IWorkerInfo manager, IEnumerable<IWorkerInfo> workers, IWorkerInfo loadFactorMinWorker)
        {
            var activityId = Guid.NewGuid().ToString();
            var settings = new ScaleSettings { MaxWorkers = 2 };
            var mockManager = new Mock<MockScaleManager>(MockBehavior.Default, MockBehavior.Strict, settings) { CallBase = true };

            // Test
            using (var scaleManager = mockManager.Object)
            {
                // Setup
                if (loadFactorMinWorker != null)
                {
                    scaleManager.MockScaleTracer.Setup(t => t.TraceInformation(activityId, loadFactorMinWorker, It.Is<string>(s => s.Contains("have int.MinValue loadfactor"))));

                    mockManager.Setup(m => m.MockRequestAddWorker(activityId, workers, manager, true))
                        .Returns(Task.FromResult(true));

                    mockManager.Setup(m => m.MockRequestRemoveWorker(activityId, manager, loadFactorMinWorker))
                        .Returns(Task.FromResult(true));
                }

                // test
                var actual = await scaleManager.MockTrySwapIfLoadFactorMinWorker(activityId, workers, manager);

                // assert
                mockManager.VerifyAll();
                scaleManager.VerifyAll();
                Assert.Equal(loadFactorMinWorker != null, actual);
            }
        }

        public static IEnumerable<object[]> BasicData
        {
            get
            {
                var homeNormal = new MockWorkerInfo { StampName = "home-stamp", WorkerName = "1", LoadFactor = 50 };
                var homeMin = new MockWorkerInfo { StampName = "home-stamp", WorkerName = "2", LoadFactor = int.MinValue };
                var slaveMin = new MockWorkerInfo { StampName = "slave-stamp", WorkerName = "2", LoadFactor = int.MinValue };

                yield return new object[] { homeNormal, new[] { homeNormal }, null };
                yield return new object[] { homeNormal, new[] { homeNormal, homeMin }, homeMin };
                yield return new object[] { homeNormal, new[] { homeNormal, homeMin, slaveMin }, slaveMin };
            }
        }
    }
}