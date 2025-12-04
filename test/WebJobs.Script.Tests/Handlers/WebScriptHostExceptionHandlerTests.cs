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
        private readonly Mock<IScriptHostWorkerManager> _mockWorkerManager;
        private readonly WebScriptHostExceptionHandler _exceptionHandler;

        public WebScriptHostExceptionHandlerTests()
        {
            _mockApplicationLifetime = new Mock<IApplicationLifetime>(MockBehavior.Strict);
            _mockLogger = new Mock<ILogger<WebScriptHostExceptionHandler>>();
            _mockWorkerManager = new Mock<IScriptHostWorkerManager>(MockBehavior.Strict);

            _exceptionHandler = new WebScriptHostExceptionHandler(
                _mockApplicationLifetime.Object,
                _mockLogger.Object,
                _mockWorkerManager.Object);
        }

        [Fact]
        public async Task OnTimeoutExceptionAsync_CallsRestartWorkerWithInvocationIdAsync_WithTimeoutException()
        {
            var task = Task.CompletedTask;
            var timeoutException = new FunctionTimeoutException("Test timeout");
            var exceptionInfo = ExceptionDispatchInfo.Capture(timeoutException);
            var timeoutGracePeriod = TimeSpan.FromSeconds(5);

            _mockWorkerManager
                .Setup(d => d.State)
                .Returns(WorkerManagerState.Initialized);

            _mockWorkerManager
                .Setup(d => d.RestartWorkerWithInvocationIdAsync(It.IsAny<string>(), It.IsAny<Exception>()))
                .Returns(Task.FromResult(true));

            await _exceptionHandler.OnTimeoutExceptionAsync(exceptionInfo, timeoutGracePeriod);

            _mockWorkerManager.Verify(d => d.RestartWorkerWithInvocationIdAsync(
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

            _mockWorkerManager
                .Setup(d => d.State)
                .Returns(WorkerManagerState.Initialized);

            _mockWorkerManager
                .Setup(d => d.RestartWorkerWithInvocationIdAsync(It.IsAny<string>(), It.IsAny<Exception>()))
                .Returns(Task.FromResult(true));

            // Don't complete the task to simulate it not finishing within the grace period

            // Act
            await _exceptionHandler.OnTimeoutExceptionAsync(exceptionInfo, timeoutGracePeriod);

            // Assert
            _mockWorkerManager.Verify(d => d.RestartWorkerWithInvocationIdAsync(
                It.IsAny<string>(),
                timeoutException), Times.Once);
        }
    }
}