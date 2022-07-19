// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Google.Protobuf.WellKnownTypes;
using Microsoft.Azure.Functions.WorkerHarness.Grpc.Messages;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using WorkerHarness.Core.Actions;
using WorkerHarness.Core.Parsing;
using WorkerHarness.Core.StreamingMessageService;
using WorkerHarness.Core.Variables;
using WorkerHarness.Core.WorkerProcess;

namespace WorkerHarness.Core.Tests.Actions
{
    [TestClass]
    public class TerminateActionTests
    {
        [TestMethod]
        public async Task ExecuteAsync_WorkerProcessNotExited_ActionResultHasStatusFailure()
        {
            // Arrange
            int gracePeriod = 1;

            Channel<StreamingMessage> outboundChannel = Channel.CreateUnbounded<StreamingMessage>();

            var mockStreamingMessageProvider = new Mock<IStreamingMessageProvider>();
            mockStreamingMessageProvider.Setup(x => x.Create(It.IsAny<string>(), It.IsAny<JsonNode>()))
                .Returns(new StreamingMessage
                {
                    WorkerTerminate = new WorkerTerminate
                    {
                        GracePeriod = new Duration { Seconds = gracePeriod }
                    }
                });

            var mockILogger = new LoggerFactory().CreateLogger<TerminateAction>();

            var mockIWorkerProcess = new Mock<IWorkerProcess>();
            mockIWorkerProcess.Setup(x => x.WaitForProcessExit(gracePeriod * 1000));
            mockIWorkerProcess.Setup(x => x.HasExited).Returns(false);

            ExecutionContext executionContext = new(new Mock<IVariableObservable>().Object,
                new Mock<IScenarioParser>().Object, mockIWorkerProcess.Object);

            TerminateAction action = new(gracePeriod, outboundChannel,
                mockStreamingMessageProvider.Object, mockILogger);

            // Act
            ActionResult actionResult = await action.ExecuteAsync(executionContext);

            // Assert
            Assert.AreEqual(StatusCode.Failure, actionResult.Status);
        }

        [TestMethod]
        public async Task ExecuteAsync_WorkerProcessExited_ActionResultHasStatusSuccess()
        {
            // Arrange
            int gracePeriod = 1;

            Channel<StreamingMessage> outboundChannel = Channel.CreateUnbounded<StreamingMessage>();

            var mockStreamingMessageProvider = new Mock<IStreamingMessageProvider>();
            mockStreamingMessageProvider.Setup(x => x.Create(It.IsAny<string>(), It.IsAny<JsonNode>()))
                .Returns(new StreamingMessage
                {
                    WorkerTerminate = new WorkerTerminate
                    {
                        GracePeriod = new Duration { Seconds = gracePeriod }
                    }
                });

            var mockILogger = new LoggerFactory().CreateLogger<TerminateAction>();

            var mockIWorkerProcess = new Mock<IWorkerProcess>();
            mockIWorkerProcess.Setup(x => x.WaitForProcessExit(gracePeriod * 1000));
            mockIWorkerProcess.Setup(x => x.HasExited).Returns(true);

            ExecutionContext executionContext = new(new Mock<IVariableObservable>().Object,
                new Mock<IScenarioParser>().Object, mockIWorkerProcess.Object);

            TerminateAction action = new(gracePeriod, outboundChannel,
                mockStreamingMessageProvider.Object, mockILogger);

            // Act
            ActionResult actionResult = await action.ExecuteAsync(executionContext);

            // Assert
            Assert.AreEqual(StatusCode.Success, actionResult.Status);
        }
    }
}
