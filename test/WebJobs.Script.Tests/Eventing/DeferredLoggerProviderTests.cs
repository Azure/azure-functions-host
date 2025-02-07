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
            provider.ProcessBufferedLogs(new List<ILoggerProvider>(), true); // Disable the provider

            await Task.Delay(1000);
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
            provider.ProcessBufferedLogs(new List<ILoggerProvider>(), true); // Process immediately

            // Wait for forwarding task to complete
            await Task.Delay(100); // Small delay to ensure the logs are processed

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
        public void ProcessBufferedLogs_ThrowsNoExceptionsWhenChannelIsEmpty()
        {
            var testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");

            // Arrange
            var provider = new DeferredLoggerProvider(testEnvironment);
            var mockLoggerProvider = new Mock<ILoggerProvider>();

            // Act & Assert (no exceptions should be thrown)
            provider.ProcessBufferedLogs(new[] { mockLoggerProvider.Object }, true);
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
            provider.ProcessBufferedLogs(new List<ILoggerProvider>() { testLoggerProvider }, true); // Process immediately

            // Wait a short period to ensure the channel is completed
            await Task.Delay(100);
            Assert.Equal(0, provider.Count); // Ensure channel is drained

            // Assert that the log was forwarded to the testLoggerProvider
            Assert.Equal(1, testLoggerProvider.GetAllLogMessages().Count);
            Assert.Equal(Assert.Single(testLoggerProvider.GetAllLogMessages()).FormattedMessage, "Error Log");

            // Ensure that the LoggerProvider is not disabled and still returns DeferredLogger
            Assert.True(provider.CreateLogger("TestCategory") is DeferredLogger);
        }
    }
}