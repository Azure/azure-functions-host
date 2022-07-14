// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.


using Microsoft.Azure.Functions.WorkerHarness.Grpc.Messages;
using Moq;
using WorkerHarness.Core.Actions;
using WorkerHarness.Core.Matching;

namespace WorkerHarness.Core.Tests.Matching
{
    [TestClass]
    public class MessageMatcherTests
    {
        [TestMethod]
        public void Match_MessageTypesDoNotMatch_ReturnFalse()
        {
            // Arrange
            IContextMatcher contextMatcher = new Mock<IContextMatcher>().Object;
            MessageMatcher matcher = new(contextMatcher);

            RpcActionMessage rpcActionMessage = new()
            {
                MessageType = "WorkerInitRequest"
            };

            StreamingMessage streamingMessage = new()
            {
                FunctionLoadRequest = new FunctionLoadRequest()
            };

            // Act
            bool matched = matcher.Match(rpcActionMessage, streamingMessage);

            // Assert
            Assert.IsFalse(matched);
        }

        [TestMethod]
        public void Match_ContextMatcherReturnsFalse_ReturnFalse()
        {
            // Arrange
            var contextMatcher = new Mock<IContextMatcher>();
            contextMatcher.Setup(x => x.MatchAll(It.IsAny<IEnumerable<MatchingContext>>(), It.IsAny<StreamingMessage>()))
                .Returns(false);

            MessageMatcher matcher = new(contextMatcher.Object);

            RpcActionMessage rpcActionMessage = new()
            {
                MessageType = "WorkerInitRequest"
            };

            StreamingMessage streamingMessage = new()
            {
                WorkerInitRequest = new WorkerInitRequest()
            };

            // Act
            bool matched = matcher.Match(rpcActionMessage, streamingMessage);

            // Assert
            Assert.IsFalse(matched);
        }

        [TestMethod]
        public void Match_ContextMatcherReturnsFalse_ReturnTrue()
        {
            // Arrange
            var contextMatcher = new Mock<IContextMatcher>();
            contextMatcher.Setup(x => x.MatchAll(It.IsAny<IEnumerable<MatchingContext>>(), It.IsAny<StreamingMessage>()))
                .Returns(true);

            MessageMatcher matcher = new(contextMatcher.Object);

            RpcActionMessage rpcActionMessage = new()
            {
                MessageType = "WorkerInitRequest"
            };

            StreamingMessage streamingMessage = new()
            {
                WorkerInitRequest = new WorkerInitRequest()
            };

            // Act
            bool matched = matcher.Match(rpcActionMessage, streamingMessage);

            // Assert
            Assert.IsTrue(matched);
        }
    }
}
