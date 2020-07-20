// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Workers;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.Http
{
    public class HttpWorkerChannelTests
    {
        private readonly IMetricsLogger _metricsLogger;
        private readonly IScriptEventManager _eventManager;

        public HttpWorkerChannelTests()
        {
            _metricsLogger = new TestMetricsLogger();
            Mock<IScriptEventManager> mockEventManager = new Mock<IScriptEventManager>();
            mockEventManager.Setup(a => a.Publish(It.IsAny<ScriptEvent>()));
            _eventManager = mockEventManager.Object;
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task TestStartWorkerProcess(bool isWorkerReady)
        {
            Mock<IWorkerProcess> workerProcess = new Mock<IWorkerProcess>();
            Mock<IHttpWorkerService> httpWorkerService = new Mock<IHttpWorkerService>();

            httpWorkerService.Setup(a => a.IsWorkerReady(It.IsAny<CancellationToken>())).Returns(Task.FromResult(isWorkerReady));
            workerProcess.Setup(a => a.StartProcessAsync()).Returns(Task.CompletedTask);
            TestLogger logger = new TestLogger("HttpWorkerChannel");
            IHttpWorkerChannel testWorkerChannel = new HttpWorkerChannel("RandomWorkerId", _eventManager, workerProcess.Object, httpWorkerService.Object, logger, _metricsLogger, 3);
            Task resultTask = null;
            try
            {
                resultTask = testWorkerChannel.StartWorkerProcessAsync();
                await resultTask;
                logger.GetLogMessages().Select(a => a.FormattedMessage).Contains("HttpWorker is Initialized.");
                Assert.Equal(resultTask.Status, TaskStatus.RanToCompletion);
            }
            catch (Exception)
            {
                logger.GetLogMessages().Select(a => a.FormattedMessage).Contains("Failed to start http worker process. workerId:");
                Assert.NotEqual(resultTask.Status, TaskStatus.RanToCompletion);
            }
        }

        [Fact]
        public async Task TestStartWorkerProcess_WorkerServiceThrowsException()
        {
            Mock<IWorkerProcess> workerProcess = new Mock<IWorkerProcess>();
            Mock<IHttpWorkerService> httpWorkerService = new Mock<IHttpWorkerService>();
            IMetricsLogger metricsLogger = new TestMetricsLogger();

            httpWorkerService.Setup(a => a.IsWorkerReady(It.IsAny<CancellationToken>())).Throws(new Exception("RandomException"));
            workerProcess.Setup(a => a.StartProcessAsync()).Returns(Task.CompletedTask);
            TestLogger logger = new TestLogger("HttpWorkerChannel");
            IHttpWorkerChannel testWorkerChannel = new HttpWorkerChannel("RandomWorkerId", _eventManager, workerProcess.Object, httpWorkerService.Object, logger, _metricsLogger, 3);
            Task resultTask = null;
            try
            {
                resultTask = testWorkerChannel.StartWorkerProcessAsync();
                await resultTask;
            }
            catch (Exception)
            {
                logger.GetLogMessages().Select(a => a.FormattedMessage).Contains("Failed to start http worker process. workerId:");
                Assert.NotEqual(resultTask.Status, TaskStatus.RanToCompletion);
            }
        }

        [Fact]
        public async Task TestStartWorkerProcess_WorkerProcessThrowsException()
        {
            Mock<IWorkerProcess> workerProcess = new Mock<IWorkerProcess>();
            Mock<IHttpWorkerService> httpWorkerService = new Mock<IHttpWorkerService>();
            IMetricsLogger metricsLogger = new TestMetricsLogger();

            httpWorkerService.Setup(a => a.IsWorkerReady(It.IsAny<CancellationToken>())).Throws(new Exception("RandomException"));
            workerProcess.Setup(a => a.StartProcessAsync()).Throws(new Exception("RandomException"));
            TestLogger logger = new TestLogger("HttpWorkerChannel");
            IHttpWorkerChannel testWorkerChannel = new HttpWorkerChannel("RandomWorkerId", _eventManager, workerProcess.Object, httpWorkerService.Object, logger, _metricsLogger, 3);
            Task resultTask = null;
            try
            {
                resultTask = testWorkerChannel.StartWorkerProcessAsync();
                await resultTask;
            }
            catch (Exception)
            {
                logger.GetLogMessages().Select(a => a.FormattedMessage).Contains("Failed to start http worker process. workerId:");
                Assert.NotEqual(resultTask.Status, TaskStatus.RanToCompletion);
            }
        }
    }
}
