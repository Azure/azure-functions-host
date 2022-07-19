// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.Functions.WorkerHarness.Grpc.Messages;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using WorkerHarness.Core.Actions;
using WorkerHarness.Core.GrpcService;
using WorkerHarness.Core.StreamingMessageService;

namespace WorkerHarness.Core.Tests.Actions
{
    [TestClass]
    public class TerminateActionProviderTests
    {
        [TestMethod]
        public void Create_ActionNodeMissingGracePeriod_ThrowArgumentException()
        {
            // Arrange
            JsonNode stubActionNode = new JsonObject();

            Channel<StreamingMessage> InboundChannel = Channel.CreateUnbounded<StreamingMessage>();
            Channel<StreamingMessage> OutboundChannel = Channel.CreateUnbounded<StreamingMessage>();
            GrpcServiceChannel stubGrpcServiceChannel = new(InboundChannel, OutboundChannel);
            IStreamingMessageProvider messageProvider = new Mock<IStreamingMessageProvider>().Object;
            ILoggerFactory loggerFactory = new LoggerFactory();

            TerminateActionProvider provider = new(stubGrpcServiceChannel, messageProvider, loggerFactory);

            // Act
            try
            {
                provider.Create(stubActionNode);
            }
            catch (ArgumentException ex)
            {
                StringAssert.Contains(ex.Message, TerminateActionProvider.MissingGracePeriodInSeconds);
                return;
            }

            Assert.Fail($"The expected {typeof(ArgumentException)} exception is not thrown");
        }

        [TestMethod]
        public void Create_GracePeriodInSecondsIsNotInteger_ThrowArgumentException()
        {
            // Arrange
            JsonNode stubActionNode = new JsonObject
            {
                ["gracePeriodInSeconds"] = "5"
            };

            Channel<StreamingMessage> InboundChannel = Channel.CreateUnbounded<StreamingMessage>();
            Channel<StreamingMessage> OutboundChannel = Channel.CreateUnbounded<StreamingMessage>();
            GrpcServiceChannel stubGrpcServiceChannel = new(InboundChannel, OutboundChannel);
            IStreamingMessageProvider messageProvider = new Mock<IStreamingMessageProvider>().Object;
            ILoggerFactory loggerFactory = new LoggerFactory();

            TerminateActionProvider provider = new(stubGrpcServiceChannel, messageProvider, loggerFactory);

            // Act
            try
            {
                provider.Create(stubActionNode);
            }
            catch (ArgumentException ex)
            {
                StringAssert.Contains(ex.Message, TerminateActionProvider.GracePeriodIsNotInteger);
                return;
            }

            Assert.Fail($"The expected {typeof(ArgumentException)} exception is not thrown");
        }

        [TestMethod]
        public void Create_GracePeriodInSecondsIsNotAValue_ThrowArgumentException()
        {

            // Arrange
            JsonNode stubActionNode = new JsonObject
            {
                ["gracePeriodInSeconds"] = new JsonObject()
            };

            Channel<StreamingMessage> InboundChannel = Channel.CreateUnbounded<StreamingMessage>();
            Channel<StreamingMessage> OutboundChannel = Channel.CreateUnbounded<StreamingMessage>();
            GrpcServiceChannel stubGrpcServiceChannel = new(InboundChannel, OutboundChannel);
            IStreamingMessageProvider messageProvider = new Mock<IStreamingMessageProvider>().Object;
            ILoggerFactory loggerFactory = new LoggerFactory();

            TerminateActionProvider provider = new(stubGrpcServiceChannel, messageProvider, loggerFactory);

            // Act
            try
            {
                provider.Create(stubActionNode);
            }
            catch (ArgumentException ex)
            {
                StringAssert.Contains(ex.Message, TerminateActionProvider.GracePeriodIsNotInteger);
                return;
            }

            Assert.Fail($"The expected {typeof(ArgumentException)} exception is not thrown");
        }

        [TestMethod]
        public void Create_GracePeriodInSecondsIsNegative_ThrowArgumentException()
        {

            // Arrange
            JsonNode stubActionNode = new JsonObject
            {
                ["gracePeriodInSeconds"] = -10
            };

            Channel<StreamingMessage> InboundChannel = Channel.CreateUnbounded<StreamingMessage>();
            Channel<StreamingMessage> OutboundChannel = Channel.CreateUnbounded<StreamingMessage>();
            GrpcServiceChannel stubGrpcServiceChannel = new(InboundChannel, OutboundChannel);
            IStreamingMessageProvider messageProvider = new Mock<IStreamingMessageProvider>().Object;
            ILoggerFactory loggerFactory = new LoggerFactory();

            TerminateActionProvider provider = new(stubGrpcServiceChannel, messageProvider, loggerFactory);

            // Act
            try
            {
                provider.Create(stubActionNode);
            }
            catch (ArgumentException ex)
            {
                StringAssert.Contains(ex.Message, TerminateActionProvider.GracePeriodIsNegative);
                return;
            }

            Assert.Fail($"The expected {typeof(ArgumentException)} exception is not thrown");
        }

        [TestMethod]
        public void Create_ReturnsTerminateAction()
        {
            // Arrange
            int gracePeriod = 10;
            JsonNode stubActionNode = new JsonObject
            {
                ["gracePeriodInSeconds"] = gracePeriod
            };

            Channel<StreamingMessage> InboundChannel = Channel.CreateUnbounded<StreamingMessage>();
            Channel<StreamingMessage> OutboundChannel = Channel.CreateUnbounded<StreamingMessage>();
            GrpcServiceChannel stubGrpcServiceChannel = new(InboundChannel, OutboundChannel);
            IStreamingMessageProvider messageProvider = new Mock<IStreamingMessageProvider>().Object;
            ILoggerFactory loggerFactory = new LoggerFactory();

            TerminateActionProvider provider = new(stubGrpcServiceChannel, messageProvider, loggerFactory);

            // Act
            IAction action = provider.Create(stubActionNode);

            // Assert
            Assert.IsTrue(action is TerminateAction);
            TerminateAction terminateAction = (TerminateAction)action;
            Assert.AreEqual(ActionTypes.Terminate, terminateAction.Type);
            Assert.AreEqual(gracePeriod, terminateAction.GracePeriodInSeconds);
        }
    }
}
