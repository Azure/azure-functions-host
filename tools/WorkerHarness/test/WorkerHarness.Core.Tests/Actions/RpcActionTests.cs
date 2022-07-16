// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.Functions.WorkerHarness.Grpc.Messages;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using WorkerHarness.Core.Actions;
using WorkerHarness.Core.Matching;
using WorkerHarness.Core.Parsing;
using WorkerHarness.Core.StreamingMessageService;
using WorkerHarness.Core.Validators;
using WorkerHarness.Core.Variables;

namespace WorkerHarness.Core.Tests.Actions
{
    [TestClass]
    public class RpcActionTests
    {
        // test 1: time out => Message not send error
        [TestMethod]
        public async Task ExecuteAsync_TimeoutOccurs_MessageNotSentError()
        {
            // Arrange
            var mockIValidatorFactory = new Mock<IValidatorFactory>();

            var mockMessageMatcher = new Mock<IMessageMatcher>();
            mockMessageMatcher.Setup(x => x.Match(It.IsAny<RpcActionMessage>(), It.IsAny<StreamingMessage>()))
                .Returns(true);

            var mockIVariableObservable = new Mock<IVariableObservable>();
            var mockIScenarioParser = new Mock<IScenarioParser>();
            ExecutionContext context = new(mockIVariableObservable.Object, mockIScenarioParser.Object);

            StreamingMessage message;
            var mockIStreamingMessageProvider = new Mock<IStreamingMessageProvider>();
            mockIStreamingMessageProvider
                .Setup(x => x.TryCreate(out message, It.IsAny<string>(), It.IsAny<JsonNode?>(), context.GlobalVariables))
                .Returns(false);

            var stubInboundChannel = Channel.CreateUnbounded<StreamingMessage>();
            var stubOutboundChannel = Channel.CreateUnbounded<StreamingMessage>();
            var stubActionData = new RpcActionData()
            {
                ActionName = "Demo action",
                Messages = new List<RpcActionMessage>()
                {
                    new RpcActionMessage() { Direction = "incoming" },
                    new RpcActionMessage() { Direction = "outgoing" }
                },
            };

            var logger = new LoggerFactory().CreateLogger<RpcAction>();

            RpcAction action = new (
                mockIValidatorFactory.Object,
                mockMessageMatcher.Object,
                mockIStreamingMessageProvider.Object,
                stubActionData,
                stubInboundChannel,
                stubOutboundChannel,
                logger
            );

            await stubInboundChannel.Writer.WriteAsync(new StreamingMessage());

            // Act
            ActionResult actionResult = await action.ExecuteAsync(context);

            // Assert
            Assert.AreEqual(StatusCode.Failure, actionResult.Status);
        }

        // test 2: time out => mesage not received
        [TestMethod]
        public async Task ExecuteAsync_TimeoutOccurs_MessageNotReceivedError()
        {
            // Arrange
            var mockIValidatorFactory = new Mock<IValidatorFactory>();

            var mockMessageMatcher = new Mock<IMessageMatcher>();
            mockMessageMatcher.Setup(x => x.Match(It.IsAny<RpcActionMessage>(), It.IsAny<StreamingMessage>()))
                .Returns(false);

            var mockIVariableObservable = new Mock<IVariableObservable>();
            var mockIScenarioParser = new Mock<IScenarioParser>();
            ExecutionContext context = new(mockIVariableObservable.Object, mockIScenarioParser.Object);

            StreamingMessage message;
            var mockIStreamingMessageProvider = new Mock<IStreamingMessageProvider>();
            mockIStreamingMessageProvider
                .Setup(x => x.TryCreate(out message, It.IsAny<string>(), It.IsAny<JsonNode?>(), context.GlobalVariables))
                .Returns(true);

            var stubInboundChannel = Channel.CreateUnbounded<StreamingMessage>();
            var stubOutboundChannel = Channel.CreateUnbounded<StreamingMessage>();
            var stubActionData = new RpcActionData()
            {
                ActionName = "Demo action",
                Messages = new List<RpcActionMessage>()
                {
                    new RpcActionMessage() { Direction = "incoming" },
                    new RpcActionMessage() { Direction = "outgoing" }
                },
            };

            var logger = new LoggerFactory().CreateLogger<RpcAction>();

            RpcAction action = new(
                mockIValidatorFactory.Object,
                mockMessageMatcher.Object,
                mockIStreamingMessageProvider.Object,
                stubActionData,
                stubInboundChannel,
                stubOutboundChannel,
                logger
            );

            await stubInboundChannel.Writer.WriteAsync(new StreamingMessage());

            // Act
            ActionResult actionResult = await action.ExecuteAsync(context);

            // Assert
            Assert.AreEqual(StatusCode.Failure, actionResult.Status);
        }

        // test 3: validation error
        [TestMethod]
        public async Task ExecuteAsync_ValidationFails_ValidationError()
        {
            // Arrange
            var mockIValidator = new Mock<IValidator>();
            mockIValidator.Setup(x => x.Validate(It.IsAny<ValidationContext>(), It.IsAny<object>()))
                .Returns(false);

            var mockIValidatorFactory = new Mock<IValidatorFactory>();
            mockIValidatorFactory.Setup(x => x.Create(It.IsAny<string>()))
                .Returns(mockIValidator.Object);

            var mockMessageMatcher = new Mock<IMessageMatcher>();
            mockMessageMatcher.Setup(x => x.Match(It.IsAny<RpcActionMessage>(), It.IsAny<StreamingMessage>()))
                .Returns(true);

            var mockIVariableObservable = new Mock<IVariableObservable>();
            var mockIScenarioParser = new Mock<IScenarioParser>();
            ExecutionContext context = new(mockIVariableObservable.Object, mockIScenarioParser.Object);

            StreamingMessage message;
            var mockIStreamingMessageProvider = new Mock<IStreamingMessageProvider>();
            mockIStreamingMessageProvider
                .Setup(x => x.TryCreate(out message, It.IsAny<string>(), It.IsAny<JsonNode?>(), context.GlobalVariables))
                .Returns(true);

            var stubInboundChannel = Channel.CreateUnbounded<StreamingMessage>();
            var stubOutboundChannel = Channel.CreateUnbounded<StreamingMessage>();
            var stubActionData = new RpcActionData()
            {
                ActionName = "Demo action",
                Messages = new List<RpcActionMessage>()
                {
                    new RpcActionMessage() { 
                        Direction = "incoming",
                        Validators = new List<ValidationContext>() { new ValidationContext() }
                    },
                    new RpcActionMessage() { Direction = "outgoing" }
                },
            };

            var logger = new LoggerFactory().CreateLogger<RpcAction>();

            RpcAction action = new(
                mockIValidatorFactory.Object,
                mockMessageMatcher.Object,
                mockIStreamingMessageProvider.Object,
                stubActionData,
                stubInboundChannel,
                stubOutboundChannel,
                logger
            );

            await stubInboundChannel.Writer.WriteAsync(new StreamingMessage());

            // Act
            ActionResult actionResult = await action.ExecuteAsync(context);

            // Assert
            Assert.AreEqual(StatusCode.Failure, actionResult.Status);
        }

        // test 4: all messages are sent, received, and validated
        [TestMethod]
        public async Task ExecuteAsync_ActionExecuteSuccessfully_ActionResultHasStatusSuccess()
        {
            // Arrange
            var mockIValidator = new Mock<IValidator>();
            mockIValidator.Setup(x => x.Validate(It.IsAny<ValidationContext>(), It.IsAny<object>()))
                .Returns(true);

            var mockIValidatorFactory = new Mock<IValidatorFactory>();
            mockIValidatorFactory.Setup(x => x.Create(It.IsAny<string>()))
                .Returns(mockIValidator.Object);

            var mockMessageMatcher = new Mock<IMessageMatcher>();
            mockMessageMatcher.Setup(x => x.Match(It.IsAny<RpcActionMessage>(), It.IsAny<StreamingMessage>()))
                .Returns(true);

            var mockIVariableObservable = new Mock<IVariableObservable>();
            var mockIScenarioParser = new Mock<IScenarioParser>();
            ExecutionContext context = new(mockIVariableObservable.Object, mockIScenarioParser.Object);

            StreamingMessage message;
            var mockIStreamingMessageProvider = new Mock<IStreamingMessageProvider>();
            mockIStreamingMessageProvider
                .Setup(x => x.TryCreate(out message, It.IsAny<string>(), It.IsAny<JsonNode?>(), context.GlobalVariables))
                .Returns(true);

            var stubInboundChannel = Channel.CreateUnbounded<StreamingMessage>();
            var stubOutboundChannel = Channel.CreateUnbounded<StreamingMessage>();
            var stubActionData = new RpcActionData()
            {
                ActionName = "Demo action",
                Messages = new List<RpcActionMessage>()
                {
                    new RpcActionMessage() {
                        Direction = "incoming",
                        Validators = new List<ValidationContext>() { new ValidationContext() }
                    },
                    new RpcActionMessage() { Direction = "outgoing" }
                },
            };

            var logger = new LoggerFactory().CreateLogger<RpcAction>();

            RpcAction action = new(
                mockIValidatorFactory.Object,
                mockMessageMatcher.Object,
                mockIStreamingMessageProvider.Object,
                stubActionData,
                stubInboundChannel,
                stubOutboundChannel,
                logger
            );

            await stubInboundChannel.Writer.WriteAsync(new StreamingMessage());

            // Act
            ActionResult actionResult = await action.ExecuteAsync(context);

            // Assert
            Assert.AreEqual(StatusCode.Success, actionResult.Status);
        }
    }
}
