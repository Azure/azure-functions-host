// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.Rpc
{
    public class RpcWorkerChannelExtensionsTests
    {
        private readonly Mock<ILogger> _mockLogger = new Mock<ILogger>();

        [Fact]
        public void ShutdownAndDispose_CallsShutdownAndDispose()
        {
            var mockChannel = new Mock<IRpcWorkerChannel>();
            mockChannel.Setup(c => c.Id).Returns("testChannel");
            var disposable = mockChannel.As<IDisposable>();

            var exception = new InvalidOperationException("test failure");

            mockChannel.Object.ShutdownAndDispose(exception, _mockLogger.Object);

            mockChannel.Verify(c => c.Shutdown(exception), Times.Once);
            disposable.Verify(d => d.Dispose(), Times.Once);
        }

        [Fact]
        public void ShutdownAndDispose_CallsShutdownWithNull_ForGracefulShutdown()
        {
            var mockChannel = new Mock<IRpcWorkerChannel>();
            mockChannel.Setup(c => c.Id).Returns("testChannel");
            var disposable = mockChannel.As<IDisposable>();

            mockChannel.Object.ShutdownAndDispose(null, _mockLogger.Object);

            mockChannel.Verify(c => c.Shutdown(null), Times.Once);
            disposable.Verify(d => d.Dispose(), Times.Once);
        }

        [Fact]
        public void ShutdownAndDispose_DisposesEvenWhenShutdownThrows()
        {
            var mockChannel = new Mock<IRpcWorkerChannel>();
            mockChannel.Setup(c => c.Id).Returns("testChannel");
            mockChannel.Setup(c => c.Shutdown(It.IsAny<Exception>())).Throws(new InvalidOperationException("shutdown error"));
            var disposable = mockChannel.As<IDisposable>();

            mockChannel.Object.ShutdownAndDispose(null, _mockLogger.Object);

            disposable.Verify(d => d.Dispose(), Times.Once);
        }

        [Fact]
        public void ShutdownAndDispose_LogsError_WhenShutdownThrows()
        {
            var mockChannel = new Mock<IRpcWorkerChannel>();
            mockChannel.Setup(c => c.Id).Returns("testChannel");
            mockChannel.Setup(c => c.Shutdown(It.IsAny<Exception>())).Throws(new InvalidOperationException("shutdown error"));
            mockChannel.As<IDisposable>();

            mockChannel.Object.ShutdownAndDispose(null, _mockLogger.Object);

            _mockLogger.Verify(
                l => l.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("testChannel")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        [Fact]
        public void ShutdownAndDispose_DoesNotThrow_WhenChannelIsNotDisposable()
        {
            var mockChannel = new Mock<IRpcWorkerChannel>();
            mockChannel.Setup(c => c.Id).Returns("testChannel");

            var ex = Record.Exception(() => mockChannel.Object.ShutdownAndDispose(null, _mockLogger.Object));

            Assert.Null(ex);
            mockChannel.Verify(c => c.Shutdown(null), Times.Once);
        }

        [Fact]
        public void ShutdownAndDispose_ThrowsArgumentNullException_WhenChannelIsNull()
        {
            Assert.Throws<ArgumentNullException>("channel", () =>
                RpcWorkerChannelExtensions.ShutdownAndDispose(null, null, _mockLogger.Object));
        }

        [Fact]
        public void ShutdownAndDispose_ThrowsArgumentNullException_WhenLoggerIsNull()
        {
            var mockChannel = new Mock<IRpcWorkerChannel>();

            Assert.Throws<ArgumentNullException>("logger", () =>
                mockChannel.Object.ShutdownAndDispose(null, null));
        }

        [Fact]
        public void ShutdownAndDispose_CallsShutdownBeforeDispose()
        {
            var callOrder = new List<string>();

            var mockChannel = new Mock<IRpcWorkerChannel>();
            mockChannel.Setup(c => c.Id).Returns("testChannel");
            mockChannel.Setup(c => c.Shutdown(It.IsAny<Exception>()))
                .Callback(() => callOrder.Add("Shutdown"));

            var disposable = mockChannel.As<IDisposable>();
            disposable.Setup(d => d.Dispose())
                .Callback(() => callOrder.Add("Dispose"));

            mockChannel.Object.ShutdownAndDispose(new InvalidOperationException("fail"), _mockLogger.Object);

            Assert.Equal(2, callOrder.Count);
            Assert.Equal("Shutdown", callOrder[0]);
            Assert.Equal("Dispose", callOrder[1]);
        }

        [Fact]
        public void ShutdownAndDispose_CallsShutdownBeforeDispose_EvenWhenShutdownThrows()
        {
            var callOrder = new List<string>();

            var mockChannel = new Mock<IRpcWorkerChannel>();
            mockChannel.Setup(c => c.Id).Returns("testChannel");
            mockChannel.Setup(c => c.Shutdown(It.IsAny<Exception>()))
                .Callback(() =>
                {
                    callOrder.Add("Shutdown");
                    throw new InvalidOperationException("shutdown error");
                });

            var disposable = mockChannel.As<IDisposable>();
            disposable.Setup(d => d.Dispose())
                .Callback(() => callOrder.Add("Dispose"));

            mockChannel.Object.ShutdownAndDispose(null, _mockLogger.Object);

            Assert.Equal(2, callOrder.Count);
            Assert.Equal("Shutdown", callOrder[0]);
            Assert.Equal("Dispose", callOrder[1]);
        }
    }
}
