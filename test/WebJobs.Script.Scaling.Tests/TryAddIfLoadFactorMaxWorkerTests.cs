// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Scaling.Tests
{
    public class TryAddIfLoadFactorMaxWorkerTests
    {
        [Theory, MemberData(nameof(BasicData))]
        public async Task BasicTests(IWorkerInfo manager, IEnumerable<IWorkerInfo> workers, IWorkerInfo loadFactorMaxWorker)
        {
            var activityId = Guid.NewGuid().ToString();
            var mockManager = new Mock<MockScaleManager>(MockBehavior.Default) { CallBase = true };

            // Test
            using (var scaleManager = mockManager.Object)
            {
                // Setup
                if (loadFactorMaxWorker != null)
                {
                    scaleManager.MockScaleTracer.Setup(t => t.TraceInformation(activityId, loadFactorMaxWorker, It.Is<string>(s => s.Contains("have int.MaxValue loadfactor"))));

                    mockManager.Setup(m => m.MockRequestAddWorker(activityId, workers, manager, false))
                        .Returns(Task.FromResult(true));
                }

                // test
                var actual = await scaleManager.MockTryAddIfLoadFactorMaxWorker(activityId, workers, manager);

                // assert
                mockManager.VerifyAll();
                scaleManager.VerifyAll();
                Assert.Equal(loadFactorMaxWorker != null, actual);
            }
        }

        public static IEnumerable<object[]> BasicData
        {
            get
            {
                var homeNormal = new MockWorkerInfo { StampName = "home-stamp", WorkerName = "1", LoadFactor = 50 };
                var homeMax = new MockWorkerInfo { StampName = "home-stamp", WorkerName = "2", LoadFactor = int.MaxValue };

                yield return new object[] { homeNormal, new[] { homeNormal }, null };
                yield return new object[] { homeNormal, new[] { homeNormal, homeMax }, homeMax };
            }
        }
    }
}