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
    public class RequestAddWorkerTests
    {
        [Theory, MemberData(nameof(BasicData))]
        public async Task BasicTests(int maxWorkers, IEnumerable<IWorkerInfo> workers, IWorkerInfo manager, bool force, bool expected)
        {
            var activityId = Guid.NewGuid().ToString();
            var settings = new ScaleSettings { MaxWorkers = maxWorkers };

            // Test
            using (var scaleManager = new MockScaleManager(MockBehavior.Strict, settings))
            {
                if (expected)
                {
                    scaleManager.MockScaleTracer.Setup(t => t.TraceAddWorker(activityId, manager, It.IsAny<string>()));

                    scaleManager.MockScaleHandler.Setup(s => s.TryAddWorker(activityId, It.IsAny<IEnumerable<string>>(), It.IsAny<int>()))
                        .Returns(Task.FromResult("3"));
                }
                else
                {
                    scaleManager.MockScaleTracer.Setup(t => t.TraceWarning(activityId, manager, It.IsAny<string>()));

                    if (force || workers.Count() < settings.MaxWorkers)
                    {
                        scaleManager.MockScaleHandler.Setup(s => s.TryAddWorker(activityId, It.IsAny<IEnumerable<string>>(), It.IsAny<int>()))
                            .Returns(Task.FromResult(string.Empty));
                    }
                }

                // test
                var actual = await scaleManager.MockRequestAddWorker(activityId, workers, manager, force, burst: false);

                // assert
                scaleManager.VerifyAll();
                Assert.Equal(expected, actual);
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

                yield return new object[] { 4, workers, manager, false, false };
                yield return new object[] { 4, workers, manager, false, false };
                yield return new object[] { 4, workers, manager, true, false };
                yield return new object[] { 5, workers, manager, false, true };
                yield return new object[] { 4, workers, manager, true, true };
            }
        }
    }
}
