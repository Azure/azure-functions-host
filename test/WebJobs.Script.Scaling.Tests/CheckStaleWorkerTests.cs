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
    public class CheckStaleWorkerTests
    {
        [Fact]
        public async Task CheckStaleIntervalTests()
        {
            var activityId = Guid.NewGuid().ToString();
            var workerInfo = new MockWorkerInfo();
            var settings = new ScaleSettings
            {
                StaleWorkerCheckInterval = TimeSpan.FromMilliseconds(500)
            };

            using (var scaleManager = new MockScaleManager(MockBehavior.Strict, settings))
            {
                // Setup
                scaleManager.MockWorkerTable.Setup(t => t.List())
                    .Returns(Task.FromResult(Enumerable.Empty<IWorkerInfo>()));
                scaleManager.MockScaleTracer.Setup(t => t.TraceInformation(activityId, workerInfo, It.IsAny<string>()));

                // Test
                for (int i = 0; i < 10; ++i)
                {
                    await scaleManager.MockCheckStaleWorker(activityId, workerInfo);

                    await Task.Delay(100);
                }

                // Assert
                scaleManager.MockWorkerTable.Verify(t => t.List(), Times.AtLeast(1));
                scaleManager.MockWorkerTable.Verify(t => t.List(), Times.AtMost(4));
            }
        }

        [Theory, MemberData("CheckStaleData")]
        public async Task CheckStaleTests(IEnumerable<MockWorkerInfo> workers)
        {
            var activityId = Guid.NewGuid().ToString();
            var manager = workers.First();
            var mockManager = new Mock<MockScaleManager>(MockBehavior.Default) { CallBase = true };

            // Test
            using (var scaleManager = mockManager.Object)
            {
                var stales = workers.Where(w => w.IsStale);

                // Setup
                scaleManager.MockWorkerTable.Setup(t => t.List())
                    .Returns(Task.FromResult(workers.OfType<IWorkerInfo>()));
                scaleManager.MockScaleTracer.Setup(h => h.TraceInformation(activityId, manager, It.Is<string>(s => s.Contains("Stale " + stales.Count() + " workers"))));

                foreach (var stale in stales)
                {
                    var pingException = stale.Properties["PingResult"] as Exception;
                    if (pingException != null)
                    {
                        scaleManager.MockScaleHandler.Setup(h => h.PingWorker(activityId, stale))
                            .Throws(pingException);

                        scaleManager.MockScaleTracer.Setup(h => h.TraceWarning(activityId, stale, It.Is<string>(s => s.Contains("failed ping request"))));

                        mockManager.Setup(m => m.MockRequestRemoveWorker(activityId, manager, stale))
                            .Returns(Task.CompletedTask);
                    }
                    else
                    {
                        scaleManager.MockScaleHandler.Setup(h => h.PingWorker(activityId, stale))
                            .Returns(Task.FromResult((bool)stale.Properties["PingResult"]));

                        if (!(bool)stale.Properties["PingResult"])
                        {
                            scaleManager.MockWorkerTable.Setup(t => t.Delete(stale))
                                .Returns(Task.CompletedTask);

                            scaleManager.MockScaleTracer.Setup(h => h.TraceWarning(activityId, stale, It.Is<string>(s => s.Contains("does not belong"))));

                            scaleManager.MockScaleTracer.Setup(t => t.TraceRemoveWorker(activityId, stale, It.Is<string>(s => s.Contains("removed"))));
                        }
                    }
                }

                // Test
                await scaleManager.MockCheckStaleWorker(activityId, manager);

                // Assert
                mockManager.VerifyAll();
                scaleManager.VerifyAll();
            }
        }

        public static IEnumerable<object> CheckStaleData
        {
            get
            {
                var normal = new MockWorkerInfo { IsStale = false };
                var validStale = new MockWorkerInfo
                {
                    IsStale = true,
                    Properties = new Dictionary<string, object>
                    {
                        { "PingResult", true }
                    }
                };
                var invalidStale = new MockWorkerInfo
                {
                    IsStale = true,
                    Properties = new Dictionary<string, object>
                    {
                        { "PingResult", false }
                    }
                };
                var badStale = new MockWorkerInfo
                {
                    IsStale = true,
                    Properties = new Dictionary<string, object>
                    {
                        { "PingResult", new Exception() }
                    }
                };

                yield return new object[] { new[] { normal } };
                yield return new object[] { new[] { normal, validStale } };
                yield return new object[] { new[] { normal, invalidStale } };
                yield return new object[] { new[] { normal, badStale } };
            }
        }
    }
}