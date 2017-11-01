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
    public class TryRemoveIfMaxWorkersTests
    {
        [Theory, MemberData(nameof(BasicData))]
        public async Task BasicTests(int maxWorkers, IWorkerInfo manager, IEnumerable<IWorkerInfo> workers, IEnumerable<IWorkerInfo> toRemoves)
        {
            var activityId = Guid.NewGuid().ToString();
            var settings = new ScaleSettings { MaxWorkers = maxWorkers };
            var mockManager = new Mock<MockScaleManager>(MockBehavior.Default, MockBehavior.Strict, settings) { CallBase = true };

            // Test
            using (var scaleManager = mockManager.Object)
            {
                // Setup
                if (toRemoves.Any())
                {
                    scaleManager.MockScaleTracer.Setup(t => t.TraceInformation(activityId, manager, It.Is<string>(s => s.Contains("exceeds maximum number"))));
                }

                foreach (var toRemove in toRemoves)
                {
                    mockManager.Setup(m => m.MockRequestRemoveWorker(activityId, manager, toRemove))
                        .Returns(Task.CompletedTask);
                }

                // test
                var actual = await scaleManager.MockTryRemoveIfMaxWorkers(activityId, workers, manager);

                // assert
                mockManager.VerifyAll();
                scaleManager.VerifyAll();
                Assert.Equal(toRemoves.Any(), actual);
            }
        }

        public static IEnumerable<object[]> BasicData
        {
            get
            {
                var homeFree = new MockWorkerInfo { StampName = "home-stamp", WorkerName = "1", LoadFactor = 10 };
                var homeBusy = new MockWorkerInfo { StampName = "home-stamp", WorkerName = "2", LoadFactor = 90 };
                var slaveFree = new MockWorkerInfo { StampName = "slave-stamp", WorkerName = "3", LoadFactor = 10 };
                var slaveBusy = new MockWorkerInfo { StampName = "slave-stamp", WorkerName = "4", LoadFactor = 90 };
                var workers = new[] { homeFree, homeBusy, slaveFree, slaveBusy };

                yield return new object[] { 4, homeFree, workers, Enumerable.Empty<IWorkerInfo>() };
                yield return new object[] { 3, homeFree, workers, new[] { slaveFree } };
                yield return new object[] { 2, homeFree, workers, new[] { slaveFree, slaveBusy } };
                yield return new object[] { 2, slaveFree, workers, new[] { slaveFree, slaveBusy } };
            }
        }
    }
}