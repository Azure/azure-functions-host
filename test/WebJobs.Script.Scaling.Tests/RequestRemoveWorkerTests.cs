// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Scaling.Tests
{
    public class RequestRemoveWorkerTests
    {
        [Theory, MemberData("BasicData")]
        public async Task BasicTests(IWorkerInfo manager, IWorkerInfo toRemove)
        {
            var activityId = Guid.NewGuid().ToString();

            // Test
            using (var scaleManager = new MockScaleManager(MockBehavior.Strict))
            {
                scaleManager.MockScaleHandler.Setup(s => s.RemoveWorker(activityId, toRemove))
                    .Returns(Task.CompletedTask);
                scaleManager.MockWorkerTable.Setup(t => t.Delete(toRemove))
                    .Returns(Task.CompletedTask);
                scaleManager.MockScaleTracer.Setup(t => t.TraceRemoveWorker(activityId, toRemove, It.Is<string>(s => s.Contains(manager.ToDisplayString()))));

                // test
                await scaleManager.MockRequestRemoveWorker(activityId, manager, toRemove);

                // assert
                scaleManager.VerifyAll();
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
                var manager = homeFree;
                var workers = new[] { homeFree, homeBusy, slaveFree, slaveBusy };

                yield return new object[] { manager, slaveBusy };
            }
        }
    }
}