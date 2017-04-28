// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Scaling.Tests
{
    public class ProcessWorkItemTests
    {
        [Theory, MemberData("BasicData")]
        public async Task BasicTests(IWorkerInfo worker, IWorkerInfo current)
        {
            var activityId = Guid.NewGuid().ToString();
            var mockManager = new Mock<MockScaleManager> { CallBase = true };

            using (var manager = mockManager.Object)
            {
                // Setup
                mockManager.Setup(m => m.MockPingWorker(activityId, worker))
                    .Returns(Task.CompletedTask);
                mockManager.Setup(m => m.MockEnsureManager(activityId, worker))
                    .Returns(Task.FromResult<IWorkerInfo>(current));
                manager.MockWorkerInfoProvider.Setup(p => p.GetWorkerInfo(activityId))
                    .Returns(Task.FromResult<IWorkerInfo>(worker));
                if (current == worker)
                {
                    mockManager.Setup(m => m.MockMakeScaleDecision(activityId, worker))
                        .Returns(Task.CompletedTask);
                    mockManager.Setup(m => m.MockCheckStaleWorker(activityId, worker))
                        .Returns(Task.CompletedTask);
                }

                // test
                await manager.MockProcessWorkItem(activityId);

                // assert
                mockManager.VerifyAll();
                manager.VerifyAll();
            }
        }

        public static IEnumerable<object[]> BasicData
        {
            get
            {
                var worker = Mock.Of<IWorkerInfo>(w =>
                    w.StampName == "ab" &&
                    w.SiteName == "cd" &&
                    w.ToString() == "ab:cd");

                var current = Mock.Of<IWorkerInfo>(w =>
                    w.StampName == "de" &&
                    w.SiteName == "fg" &&
                    w.ToString() == "de:fg");

                yield return new object[] { worker, null };
                yield return new object[] { worker, worker };
                yield return new object[] { worker, current };
            }
        }
    }
}