// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Scale;
using Microsoft.Azure.WebJobs.Script.Scaling;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Scale
{
    public class WorkerStatusProviderTests
    {
        private readonly WorkerStatusProvider _workerStatusProvider;
        private readonly Mock<HostPerformanceManager> _mockPerformanceManager;
        private readonly TestTraceWriter _traceWriter = new TestTraceWriter(TraceLevel.Verbose);
        private readonly Mock<ILoadFactorProvider> _loadFactorProvider;

        public WorkerStatusProviderTests()
        {
            _mockPerformanceManager = new Mock<HostPerformanceManager>(MockBehavior.Strict);
            _loadFactorProvider = new Mock<ILoadFactorProvider>(MockBehavior.Strict);
            _workerStatusProvider = new WorkerStatusProvider(_mockPerformanceManager.Object, _loadFactorProvider.Object, _traceWriter);
        }

        [Fact]
        public async Task GetWorkerStatus_LowLoad_ReturnsExpectedResult()
        {
            _mockPerformanceManager.Setup(p => p.IsUnderHighLoad(It.IsAny<Collection<string>>(), It.IsAny<TraceWriter>())).Returns(false);

            string activityId = Guid.NewGuid().ToString();
            int loadFactor = await _workerStatusProvider.GetWorkerStatus(activityId);

            Assert.Equal(ScaleSettings.DefaultFreeWorkerLoadFactor, loadFactor);
        }

        [Fact]
        public async Task GetWorkerStatus_HighLoad_ReturnsExpectedResult()
        {
            _mockPerformanceManager.Setup(p => p.IsUnderHighLoad(It.IsAny<Collection<string>>(), It.IsAny<TraceWriter>())).Returns(true)
                .Callback<Collection<string>>(p =>
                {
                    p.Add("Processes");
                    p.Add("Connections");
                });

            string activityId = Guid.NewGuid().ToString();
            int loadFactor = await _workerStatusProvider.GetWorkerStatus(activityId);

            Assert.Equal(ScaleSettings.DefaultBusyWorkerLoadFactor, loadFactor);

            var traceEvent = _traceWriter.Traces.First();
            Assert.Equal(TraceLevel.Warning, traceEvent.Level);
            Assert.Equal("Thresholds for the following counters have been exceeded: [Processes, Connections]", traceEvent.Message);
        }
    }
}