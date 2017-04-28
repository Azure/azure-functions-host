// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Scaling.Tests
{
    public class EnsureManagerTests
    {
        [Theory, MemberData("EnsureManagerData")]
        public async Task BasicTests(IWorkerInfo worker, IWorkerInfo current, IEnumerable<IWorkerInfo> workers, IWorkerInfo expected)
        {
            var activityId = Guid.NewGuid().ToString();
            var mockManager = new Mock<MockScaleManager>(MockBehavior.Default) { CallBase = true };

            // Test
            using (var scaleManager = mockManager.Object)
            {
                // Setup
                IWorkerInfo newManager = null;
                if (expected != current)
                {
                    mockManager.Setup(m => m.MockSetManager(activityId, worker, current))
                        .Callback((string acticityId1, IWorkerInfo info1, IWorkerInfo current1) => newManager = info1)
                        .Returns(() => Task.FromResult(newManager));
                }

                scaleManager.MockWorkerTable.Setup(t => t.GetManager())
                    .Returns(Task.FromResult<IWorkerInfo>(current));
                if (workers != null)
                {
                    scaleManager.MockWorkerTable.Setup(t => t.List())
                        .Returns(Task.FromResult(workers));
                }

                // test
                var actual = await scaleManager.MockEnsureManager(activityId, worker);

                // assert
                mockManager.VerifyAll();
                scaleManager.VerifyAll();
                Assert.True(ScaleUtils.Equals(expected, actual));
            }
        }

        public static IEnumerable<object[]> EnsureManagerData
        {
            get
            {
                var home = Mock.Of<IWorkerInfo>(w =>
                    w.StampName == "home" &&
                    w.SiteName == "valid" &&
                    w.IsHomeStamp == true &&
                    w.IsStale == false &&
                    w.ToString() == "home:valid");

                var homeMgr = Mock.Of<IWorkerInfo>(w =>
                    w.StampName == "homeMgr" &&
                    w.SiteName == "valid" &&
                    w.IsHomeStamp == true &&
                    w.IsStale == false &&
                    w.ToString() == "homeMgr:valid");

                var staleHome = Mock.Of<IWorkerInfo>(w =>
                    w.StampName == "home" &&
                    w.SiteName == "stale" &&
                    w.IsHomeStamp == true &&
                    w.IsStale == true &&
                    w.ToString() == "home:stale");

                var slave = Mock.Of<IWorkerInfo>(w =>
                    w.StampName == "slave" &&
                    w.SiteName == "valid" &&
                    w.IsHomeStamp == false &&
                    w.IsStale == false &&
                    w.ToString() == "slave:valid");

                var slaveMgr = Mock.Of<IWorkerInfo>(w =>
                    w.StampName == "slaveMgr" &&
                    w.SiteName == "valid" &&
                    w.IsHomeStamp == false &&
                    w.IsStale == false &&
                    w.ToString() == "slaveMgr:valid");

                yield return new object[] { home, null, null, home };
                yield return new object[] { home, staleHome, null, home };
                yield return new object[] { slave, staleHome, new[] { slave, staleHome }, slave };
                yield return new object[] { slave, null, new[] { home }, null };
                yield return new object[] { home, homeMgr, null, homeMgr };
                yield return new object[] { home, slaveMgr, null, home };
                yield return new object[] { slave, slaveMgr, null, slaveMgr };
            }
        }
    }
}