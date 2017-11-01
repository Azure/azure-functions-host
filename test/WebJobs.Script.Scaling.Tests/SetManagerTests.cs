// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Scaling.Tests
{
    public class SetManagerTests
    {
        [Theory, MemberData(nameof(SuccessfulSetData))]
        public async Task SuccessfulSetTests(IWorkerInfo worker, IWorkerInfo current)
        {
            var activityId = Guid.NewGuid().ToString();
            var tableLock = new MockWorkerTableLock();

            // Test
            using (var scaleManager = new MockScaleManager(MockBehavior.Strict))
            {
                // Setup
                scaleManager.MockWorkerTable.Setup(t => t.AcquireLock())
                    .Returns(() => tableLock.AcquireLock());
                scaleManager.MockWorkerTable.Setup(t => t.GetManager())
                    .Returns(Task.FromResult(current));
                scaleManager.MockWorkerTable.Setup(t => t.SetManager(worker))
                    .Returns(Task.CompletedTask);
                scaleManager.MockScaleTracer.Setup(t => t.TraceInformation(activityId, worker, It.Is<string>(c => c.Contains("Acquire table lock"))));
                scaleManager.MockScaleTracer.Setup(t => t.TraceInformation(activityId, worker, It.Is<string>(c => c.Contains("Release table lock"))));
                scaleManager.MockScaleTracer.Setup(t => t.TraceInformation(activityId, worker, It.Is<string>(c => c.Contains("is set to be a manager"))));

                // test
                var newManager = await scaleManager.MockSetManager(activityId, worker, current);

                // assert
                scaleManager.VerifyAll();
                Assert.True(ScaleUtils.Equals(newManager, worker));
                Assert.False(ScaleUtils.Equals(newManager, current));
            }
        }

        public static IEnumerable<object[]> SuccessfulSetData
        {
            get
            {
                var worker = Mock.Of<IWorkerInfo>(w =>
                    w.StampName == "ab" &&
                    w.SiteName == "cd" &&
                    w.ToString() == "ab:cd");

                var current = Mock.Of<IWorkerInfo>(w =>
                    w.StampName == "wx" &&
                    w.SiteName == "yz" &&
                    w.ToString() == "wx:yz");

                yield return new object[] { worker, null };
                yield return new object[] { worker, current };
            }
        }

        [Theory, MemberData(nameof(AlreadySetData))]
        public async Task AlreadySetTests(IWorkerInfo worker, IWorkerInfo current, IWorkerInfo other)
        {
            var activityId = Guid.NewGuid().ToString();
            var tableLock = new MockWorkerTableLock();

            // Test
            using (var scaleManager = new MockScaleManager(MockBehavior.Strict))
            {
                // Setup
                scaleManager.MockWorkerTable.Setup(t => t.AcquireLock())
                    .Returns(() => tableLock.AcquireLock());
                scaleManager.MockWorkerTable.Setup(t => t.GetManager())
                    .Returns(Task.FromResult(other));
                scaleManager.MockScaleTracer.Setup(t => t.TraceInformation(activityId, worker, It.Is<string>(c => c.Contains("Acquire table lock"))));
                scaleManager.MockScaleTracer.Setup(t => t.TraceInformation(activityId, worker, It.Is<string>(c => c.Contains("Release table lock"))));

                // test
                var newManager = await scaleManager.MockSetManager(activityId, worker, current);

                // assert
                scaleManager.VerifyAll();
                Assert.True(ScaleUtils.Equals(newManager, other));
            }
        }

        public static IEnumerable<object[]> AlreadySetData
        {
            get
            {
                var worker = Mock.Of<IWorkerInfo>(w =>
                    w.StampName == "ab" &&
                    w.SiteName == "cd" &&
                    w.ToString() == "ab:cd");

                var current = Mock.Of<IWorkerInfo>(w =>
                    w.StampName == "ef" &&
                    w.SiteName == "gh" &&
                    w.ToString() == "ef:gh");

                var other = Mock.Of<IWorkerInfo>(w =>
                    w.StampName == "wx" &&
                    w.SiteName == "yz" &&
                    w.ToString() == "wx:yz");

                yield return new object[] { worker, null, other };
                yield return new object[] { worker, current, other };
                yield return new object[] { worker, current, null };
            }
        }
    }
}