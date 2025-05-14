// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using OpenTelemetry.Logs;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Eventing
{
    public class DeferredLoggerProviderTests
    {
        [Fact]
        public void CreateLogger_ReturnsDeferredLogger_WhenEnabled()
        {
            var testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");

            // Arrange
            var provider = new DeferredLoggerProvider(testEnvironment);

            // Act
            var logger = provider.CreateLogger("TestCategory");

            // Assert
            Assert.IsType<DeferredLogger>(logger);
        }

        [Fact]
        public async Task CreateLogger_ReturnsNullLogger_WhenDisabled()
        {
            var testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");

            // Arrange
            var provider = new DeferredLoggerProvider(testEnvironment);
            await provider.ProcessBufferedLogsAsync(new List<ILoggerProvider>());

            // Act
            var logger = provider.CreateLogger("TestCategory");

            // Assert
            Assert.IsType<NullLogger>(logger);
        }

        [Fact]
        public async Task ProcessBufferedLogs_DrainsChannelWithoutProviders()
        {
            var testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");

            // Arrange
            var provider = new DeferredLoggerProvider(testEnvironment);

            var logger = provider.CreateLogger("TestCategory");
            logger.LogInformation("Test Log Message");

            // Act
            await provider.ProcessBufferedLogsAsync(new List<ILoggerProvider>());

            // Assert
            Assert.Equal(0, provider.Count); // Ensure channel is drained
        }

        [Fact]
        public async Task Dispose_DisablesProviderAndCompletesChannel()
        {
            var testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");

            // Arrange
            var provider = new DeferredLoggerProvider(testEnvironment);
            var logger = provider.CreateLogger("TestCategory");
            logger.LogInformation("Log before disposal");

            // Act
            await provider.ProcessBufferedLogsAsync(new List<ILoggerProvider>());

            // Assert
            Assert.False(provider.CreateLogger("TestCategory") is DeferredLogger);
            Assert.Equal(0, provider.Count); // Ensure channel is drained
        }

        [Fact]
        public void Count_ShouldReturnNumberOfBufferedLogs()
        {
            var testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");

            // Arrange
            var provider = new DeferredLoggerProvider(testEnvironment);

            var logger = provider.CreateLogger("TestCategory");
            logger.LogInformation("Test Log 1");
            logger.LogInformation("Test Log 2");

            // Act
            int count = provider.Count;

            // Assert
            Assert.Equal(0, count);
        }

        [Fact]
        public void Dispose_CanBeCalledMultipleTimesWithoutException()
        {
            var testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");

            // Arrange
            var provider = new DeferredLoggerProvider(testEnvironment);

            // Act & Assert
            provider.Dispose(); // First disposal
            provider.Dispose(); // Second disposal, should not throw
        }

        [Fact]
        public async Task ProcessBufferedLogs_ThrowsNoExceptionsWhenChannelIsEmpty()
        {
            var testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");

            // Arrange
            var provider = new DeferredLoggerProvider(testEnvironment);
            var mockLoggerProvider = new Mock<ILoggerProvider>();

            var task = provider.ProcessBufferedLogsAsync(new[] { mockLoggerProvider.Object });

            // Close the channel so that ProcessBufferedLogsAsync can complete
            provider.Dispose();

            // Act & Assert (no exceptions should be thrown)
            var exception = await Record.ExceptionAsync(() => task);
            Assert.Null(exception);
        }

        [Fact]
        public async Task Dispose_DoNotDisablesProviderAndCompletesChannel()
        {
            var testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");

            // Arrange
            var provider = new DeferredLoggerProvider(testEnvironment);
            var logger = provider.CreateLogger("TestCategory");
            logger.LogError("Error Log");

            // Create an instance of IOptionsMonitor<OpenTelemetryLoggerOptions>
            var optionsMonitor = Mock.Of<IOptionsMonitor<OpenTelemetryLoggerOptions>>();

            // Pass the optionsMonitor to the OpenTelemetryLoggerProvider constructor
            //OpenTelemetryLoggerProvider openTelemetryLoggerProvider = new(optionsMonitor);
            TestLoggerProvider testLoggerProvider = new TestLoggerProvider();
            testLoggerProvider.SetScopeProvider(new LoggerExternalScopeProvider());

            // Act
            var task = provider.ProcessBufferedLogsAsync(new List<ILoggerProvider>() { testLoggerProvider });

            // Close the channel so that ProcessBufferedLogsAsync can complete
            provider.Dispose();
            await task;
            Assert.Equal(0, provider.Count); // Ensure channel is drained

            // Assert that the log was forwarded to the testLoggerProvider
            Assert.Equal(1, testLoggerProvider.GetAllLogMessages().Count);
            Assert.Equal(Assert.Single(testLoggerProvider.GetAllLogMessages()).FormattedMessage, "Error Log");
        }

        [Fact]
        public void Dispose_DoNotDisablesProvider()
        {
            var testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");

            // Arrange
            var provider = new DeferredLoggerProvider(testEnvironment);
            var logger = provider.CreateLogger("TestCategory");
            logger.LogError("Error Log");

            // Create an instance of IOptionsMonitor<OpenTelemetryLoggerOptions>
            var optionsMonitor = Mock.Of<IOptionsMonitor<OpenTelemetryLoggerOptions>>();

            // Pass the optionsMonitor to the OpenTelemetryLoggerProvider constructor
            //OpenTelemetryLoggerProvider openTelemetryLoggerProvider = new(optionsMonitor);
            TestLoggerProvider testLoggerProvider = new TestLoggerProvider();
            testLoggerProvider.SetScopeProvider(new LoggerExternalScopeProvider());

            // Act
            _ = provider.ProcessBufferedLogsAsync(new List<ILoggerProvider>() { testLoggerProvider });

            // Ensure that the LoggerProvider is not disabled and still returns DeferredLogger
            Assert.True(provider.CreateLogger("TestCategory") is DeferredLogger);
        }
    }
}