// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.Rpc
{
    public class RpcWorkerProcessTests
    {
        private RpcWorkerProcess _rpcWorkerProcess;

        private Mock<IScriptEventManager> _eventManager;

        public RpcWorkerProcessTests()
        {
            _eventManager = new Mock<IScriptEventManager>();
            var workerProcessFactory = new Mock<IWorkerProcessFactory>();
            var processRegistry = new Mock<IProcessRegistry>();
            var rpcServer = new TestRpcServer();
            var languageWorkerConsoleLogSource = new Mock<IWorkerConsoleLogSource>();
            var testEnv = new TestEnvironment();
            var testWorkerConfigs = TestHelpers.GetTestWorkerConfigs();
            var serviceProviderMock = new Mock<IServiceProvider>(MockBehavior.Strict);

            _rpcWorkerProcess = new RpcWorkerProcess("node",
                "testworkerId",
                "testrootPath",
                rpcServer.Uri,
                testWorkerConfigs.ElementAt(0),
                _eventManager.Object,
                workerProcessFactory.Object,
                processRegistry.Object,
                new TestLogger("test"),
                languageWorkerConsoleLogSource.Object,
                new TestMetricsLogger(),
                serviceProviderMock.Object);
        }

        [Fact]
        public void ErrorMessageQueue_Empty()
        {
            Assert.Empty(_rpcWorkerProcess.ProcessStdErrDataQueue);
        }

        [Fact]
        public void ErrorMessageQueue_Enqueue_Success()
        {
            WorkerProcessUtilities.AddStdErrMessage(_rpcWorkerProcess.ProcessStdErrDataQueue, "Error1");
            WorkerProcessUtilities.AddStdErrMessage(_rpcWorkerProcess.ProcessStdErrDataQueue, "Error2");

            Assert.True(_rpcWorkerProcess.ProcessStdErrDataQueue.Count == 2);
            string exceptionMessage = string.Join(",", _rpcWorkerProcess.ProcessStdErrDataQueue.Where(s => !string.IsNullOrEmpty(s)));
            Assert.Equal("Error1,Error2", exceptionMessage);
        }

        [Fact]
        public void ErrorMessageQueue_Full_Enqueue_Success()
        {
            WorkerProcessUtilities.AddStdErrMessage(_rpcWorkerProcess.ProcessStdErrDataQueue, "Error1");
            WorkerProcessUtilities.AddStdErrMessage(_rpcWorkerProcess.ProcessStdErrDataQueue, "Error2");
            WorkerProcessUtilities.AddStdErrMessage(_rpcWorkerProcess.ProcessStdErrDataQueue, "Error3");
            WorkerProcessUtilities.AddStdErrMessage(_rpcWorkerProcess.ProcessStdErrDataQueue, "Error4");
            Assert.True(_rpcWorkerProcess.ProcessStdErrDataQueue.Count == 3);
            string exceptionMessage = string.Join(",", _rpcWorkerProcess.ProcessStdErrDataQueue.Where(s => !string.IsNullOrEmpty(s)));
            Assert.Equal("Error2,Error3,Error4", exceptionMessage);
        }

        [Theory]
        [InlineData("languageWorkerConsoleLog Connection established")]
        [InlineData("LANGUAGEWORKERCONSOLELOG Connection established")]
        [InlineData("LanguageWorkerConsoleLog Connection established")]
        public void IsLanguageWorkerConsoleLog_Returns_True_RemovesLogPrefix(string msg)
        {
            Assert.True(WorkerProcessUtilities.IsConsoleLog(msg));
            Assert.Equal(" Connection established", WorkerProcessUtilities.RemoveLogPrefix(msg));
        }

        [Theory]
        [InlineData("grpc languageWorkerConsoleLog Connection established")]
        [InlineData("My secret languageWorkerConsoleLog")]
        [InlineData("Connection established")]
        public void IsLanguageWorkerConsoleLog_Returns_False(string msg)
        {
            Assert.False(WorkerProcessUtilities.IsConsoleLog(msg));
        }

        [Fact]
        public void HandleWorkerProcessExitError_PublishesWorkerRestartEvent_OnIntentionalRestartExitCode()
        {
            _rpcWorkerProcess.HandleWorkerProcessRestart();

            _eventManager.Verify(_ => _.Publish(It.IsAny<WorkerRestartEvent>()), Times.Once());
            _eventManager.Verify(_ => _.Publish(It.IsAny<WorkerErrorEvent>()), Times.Never());
        }
    }
}