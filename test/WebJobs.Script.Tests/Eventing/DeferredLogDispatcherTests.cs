// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Eventing
{
    public class DeferredLogDispatcherTests
    {
        [Fact]
        public async Task ShouldForwardLogsToLoggerProviders_WhenProvidersAreAddedAsync()
        {
            // Arrange
            var deferredLogDispatcher = new DeferredLogDispatcher();
            var logLevel = LogLevel.Information;
            var logMessage = "Test log message";
            var logCategory = "TestCategory";
            var exception = new Exception("Test exception");

            // Mock ILoggerProvider and ILogger
            var loggerProviderMock = new Mock<ILoggerProvider>();
            var loggerMock = new InMemoryLogger();

            loggerProviderMock.Setup(provider => provider.CreateLogger(It.IsAny<string>()))
                .Returns(loggerMock);

            // Add the logger provider to the dispatcher
            deferredLogDispatcher.AddLoggerProvider(loggerProviderMock.Object);

            // Act
            deferredLogDispatcher.Log(new DeferredLogEntry
            {
                EventId = new EventId(1),
                LogLevel = logLevel,
                Category = logCategory,
                Message = logMessage,
                Exception = exception
            });

            // Process buffered logs
            deferredLogDispatcher.ProcessBufferedLogs(runImmediately: true);
            await Task.Delay(1000);
            // Assert
            loggerProviderMock.Verify(provider => provider.CreateLogger(logCategory), Times.Once);

            Assert.Single(loggerMock.LogEntries);
            Assert.Equal(0, deferredLogDispatcher.Count);
            Assert.Contains(logMessage, loggerMock.LogEntries[0]);
        }

        [Fact]
        public async Task ShouldDrainLogs_WhenNoLoggerProvidersAreAddedAsync()
        {
            // Arrange
            var deferredLogDispatcher = new DeferredLogDispatcher();
            var logMessage = "Test log message";

            // Act
            deferredLogDispatcher.Log(new DeferredLogEntry
            {
                LogLevel = LogLevel.Information,
                Category = "TestCategory",
                Message = logMessage,
                Exception = null
            });

            Assert.Equal(1, deferredLogDispatcher.Count);
            // Process logs with no providers
            deferredLogDispatcher.ProcessBufferedLogs(runImmediately: true);
            await Task.Delay(1000);

            // Assert
            // No provider was added, so nothing should be forwarded.
            // We expect no exceptions to be thrown and the channel to be drained.
            Assert.Equal(0, deferredLogDispatcher.Count);
            Assert.True(true); // This is a trivial assertion to ensure test passed.
        }
    }
}