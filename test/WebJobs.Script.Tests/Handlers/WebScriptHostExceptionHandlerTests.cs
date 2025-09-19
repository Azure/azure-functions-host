// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Handlers
{
    public class WebScriptHostExceptionHandlerTests
    {
        private readonly Mock<IApplicationLifetime> _mockApplicationLifetime;
        private readonly Mock<ILogger<WebScriptHostExceptionHandler>> _mockLogger;
        private readonly Mock<IFunctionInvocationDispatcherFactory> _mockDispatcherFactory;
        private readonly Mock<IFunctionInvocationDispatcher> _mockDispatcher;
        private readonly WebScriptHostExceptionHandler _exceptionHandler;

        public WebScriptHostExceptionHandlerTests()
        {
            _mockApplicationLifetime = new Mock<IApplicationLifetime>();
            _mockLogger = new Mock<ILogger<WebScriptHostExceptionHandler>>();
            _mockDispatcherFactory = new Mock<IFunctionInvocationDispatcherFactory>();
            _mockDispatcher = new Mock<IFunctionInvocationDispatcher>();

            _mockDispatcherFactory.Setup(f => f.GetFunctionDispatcher())
                .Returns(_mockDispatcher.Object);

            _exceptionHandler = new WebScriptHostExceptionHandler(
                _mockApplicationLifetime.Object,
                _mockLogger.Object,
                _mockDispatcherFactory.Object);
        }

        [Fact]
        public async Task OnTimeoutExceptionAsync_CallsRestartWorkerWithInvocationIdAsync_WithTimeoutException()
        {
            var task = Task.CompletedTask;
            var timeoutException = new FunctionTimeoutException("Test timeout");
            var exceptionInfo = ExceptionDispatchInfo.Capture(timeoutException);
            var timeoutGracePeriod = TimeSpan.FromSeconds(5);

            _mockDispatcher.Setup(d => d.State)
                .Returns(FunctionInvocationDispatcherState.Initialized);
            _mockDispatcher.Setup(d => d.RestartWorkerWithInvocationIdAsync(It.IsAny<string>(), It.IsAny<Exception>()))
                .Returns(Task.FromResult(true));

            await _exceptionHandler.OnTimeoutExceptionAsync(exceptionInfo, timeoutGracePeriod);

            _mockDispatcher.Verify(d => d.RestartWorkerWithInvocationIdAsync(
                It.IsAny<string>(),
                timeoutException), Times.Once);
        }

        [Fact]
        public async Task OnTimeoutExceptionAsync_WhenTaskDoesNotCompleteWithinGracePeriod_RestartsWorker()
        {
            // Arrange
            var invocationId = Guid.NewGuid();
            var taskCompletionSource = new TaskCompletionSource<bool>();
            var timeoutException = new FunctionTimeoutException("Test timeout");
            var exceptionInfo = ExceptionDispatchInfo.Capture(timeoutException);
            var timeoutGracePeriod = TimeSpan.FromMilliseconds(100); // Short grace period

            _mockDispatcher.Setup(d => d.State)
                .Returns(FunctionInvocationDispatcherState.Initialized);
            _mockDispatcher.Setup(d => d.RestartWorkerWithInvocationIdAsync(It.IsAny<string>(), It.IsAny<Exception>()))
                .Returns(Task.FromResult(true));

            // Don't complete the task to simulate it not finishing within the grace period

            // Act
            await _exceptionHandler.OnTimeoutExceptionAsync(exceptionInfo, timeoutGracePeriod);

            // Assert
            _mockDispatcher.Verify(d => d.RestartWorkerWithInvocationIdAsync(
                It.IsAny<string>(),
                timeoutException), Times.Once);
        }
    }
}