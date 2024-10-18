// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Eventing
{
    public class DeferredLoggerProviderTests
    {
        [Fact]
        public void CreateLogger_ReturnsDeferredLogger_WhenEnabled()
        {
            // Arrange
            var provider = new DeferredLoggerProvider();

            // Act
            var logger = provider.CreateLogger("TestCategory");

            // Assert
            Assert.IsType<DeferredLogger>(logger);
        }

        [Fact]
        public void CreateLogger_ReturnsNullLogger_WhenDisabled()
        {
            // Arrange
            var provider = new DeferredLoggerProvider();
            provider.ProcessBufferedLogs(new List<ILoggerProvider>(), true); // Disable the provider

            // Act
            var logger = provider.CreateLogger("TestCategory");

            // Assert
            Assert.IsType<NullLogger>(logger);
        }

        [Fact]
        public async Task ProcessBufferedLogs_ForwardsLogsToProviders()
        {
            // Arrange
            var mockLoggerProvider = new Mock<ILoggerProvider>();
            var mockLogger = new Mock<ILogger>();
            mockLoggerProvider.Setup(p => p.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

            var providers = new List<ILoggerProvider> { mockLoggerProvider.Object };
            var provider = new DeferredLoggerProvider();

            var logger = provider.CreateLogger("TestCategory");

            var state = "TestMessage";
            var eventId = new EventId(1, "TestEvent");
            var exception = new InvalidOperationException("TestException");

            // Act
            logger.Log(LogLevel.Information, eventId, state, exception, (s, e) => $"{s}: {e?.Message}");

            Assert.Equal(1, provider.Count);
            provider.ProcessBufferedLogs(providers, true); // Process immediately

            // Wait for forwarding task to complete
            await Task.Delay(1000);
            Assert.Equal(0, provider.Count); // Ensure channel is drained
        }

        [Fact]
        public async Task ProcessBufferedLogs_DrainsChannelWithoutProviders()
        {
            // Arrange
            var provider = new DeferredLoggerProvider();

            var logger = provider.CreateLogger("TestCategory");
            logger.LogInformation("Test Log Message");

            // Act
            provider.ProcessBufferedLogs(new List<ILoggerProvider>(), true); // Process immediately

            // Wait for forwarding task to complete
            await Task.Delay(100); // Small delay to ensure the logs are processed

            // Assert
            Assert.Equal(0, provider.Count); // Ensure channel is drained
        }

        [Fact]
        public async Task Dispose_DisablesProviderAndCompletesChannel()
        {
            // Arrange
            var provider = new DeferredLoggerProvider();
            var logger = provider.CreateLogger("TestCategory");
            logger.LogInformation("Log before disposal");

            // Act
            provider.ProcessBufferedLogs(new List<ILoggerProvider>(), true); // Process immediately
            provider.Dispose();

            // Wait a short period to ensure the channel is completed
            await Task.Delay(100);

            // Assert
            Assert.False(provider.CreateLogger("TestCategory") is DeferredLogger);
            Assert.Equal(0, provider.Count); // Ensure channel is drained
        }

        [Fact]
        public void Count_ShouldReturnNumberOfBufferedLogs()
        {
            // Arrange
            var provider = new DeferredLoggerProvider();

            var logger = provider.CreateLogger("TestCategory");
            logger.LogInformation("Test Log 1");
            logger.LogInformation("Test Log 2");

            // Act
            int count = provider.Count;

            // Assert
            Assert.Equal(2, count);
        }

        [Fact]
        public void Dispose_CanBeCalledMultipleTimesWithoutException()
        {
            // Arrange
            var provider = new DeferredLoggerProvider();

            // Act & Assert
            provider.Dispose(); // First disposal
            provider.Dispose(); // Second disposal, should not throw
        }

        [Fact]
        public void ProcessBufferedLogs_ThrowsNoExceptionsWhenChannelIsEmpty()
        {
            // Arrange
            var provider = new DeferredLoggerProvider();
            var mockLoggerProvider = new Mock<ILoggerProvider>();

            // Act & Assert (no exceptions should be thrown)
            provider.ProcessBufferedLogs(new[] { mockLoggerProvider.Object }, true);
        }
    }
}