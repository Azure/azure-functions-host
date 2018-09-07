﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class SystemLoggerTests
    {
        private readonly SystemLogger _logger;
        private readonly Mock<IEventGenerator> _mockEventGenerator;
        private readonly string _websiteName;
        private readonly string _subscriptionId;
        private readonly string _category;
        private readonly string _functionName = "TestFunction";
        private readonly string _hostInstanceId;

        public SystemLoggerTests()
        {
            _subscriptionId = "e3235165-1600-4819-85f0-2ab362e909e4";
            _hostInstanceId = Guid.NewGuid().ToString();
            _websiteName = "functionstest";

            _mockEventGenerator = new Mock<IEventGenerator>(MockBehavior.Strict);

            var environment = new TestEnvironment(new Dictionary<string, string>
                {
                    { EnvironmentSettingNames.AzureWebsiteOwnerName,  $"{_subscriptionId}+westuswebspace" },
                    { EnvironmentSettingNames.AzureWebsiteName,  _websiteName },
                });

            _category = LogCategories.CreateFunctionCategory(_functionName);
            _logger = new SystemLogger(_hostInstanceId, _category, _mockEventGenerator.Object, environment);
        }

        [Fact]
        public void Log_Verbose_EmitsExpectedEvent()
        {
            string eventName = string.Empty;
            string details = string.Empty;
            string summary = "TestMessage";
            string functionInvocationId = string.Empty;
            string activityId = string.Empty;

            _mockEventGenerator.Setup(p => p.LogFunctionTraceEvent(LogLevel.Debug, _subscriptionId, _websiteName, _functionName, eventName, _category, details, summary, string.Empty, string.Empty, functionInvocationId, _hostInstanceId, activityId));

            _logger.LogDebug(summary);

            _mockEventGenerator.VerifyAll();
        }

        [Fact]
        public void Log_Verbose_LogData_EmitsExpectedEvent()
        {
            string eventName = string.Empty;
            string details = string.Empty;
            string message = "TestMessage";
            string functionInvocationId = Guid.NewGuid().ToString();
            string activityId = Guid.NewGuid().ToString();
            var scopeState = new Dictionary<string, object>
            {
                [ScriptConstants.LogPropertyFunctionInvocationIdKey] = functionInvocationId
            };

            _mockEventGenerator.Setup(p => p.LogFunctionTraceEvent(LogLevel.Debug, _subscriptionId, _websiteName, _functionName, eventName, _category, details, message, string.Empty, string.Empty, functionInvocationId, _hostInstanceId, activityId));

            var logData = new Dictionary<string, object>
            {
                [ScriptConstants.LogPropertyActivityIdKey] = activityId
            };

            using (_logger.BeginScope(scopeState))
            {
                _logger.Log(LogLevel.Debug, 0, logData, null, (state, ex) => message);
            }

            _mockEventGenerator.VerifyAll();
        }

        [Fact]
        public void Log_Error_EmitsExpectedEvent()
        {
            string eventName = string.Empty;
            string message = "TestMessage";
            string functionInvocationId = string.Empty;
            string activityId = string.Empty;

            Exception ex = new Exception("Kaboom");

            _mockEventGenerator.Setup(p => p.LogFunctionTraceEvent(LogLevel.Error, _subscriptionId, _websiteName, _functionName, eventName, _category, ex.ToFormattedString(), message, ex.GetType().ToString(), ex.Message, functionInvocationId, _hostInstanceId, activityId));

            _logger.LogError(ex, message);

            _mockEventGenerator.VerifyAll();
        }

        [Fact]
        public void Log_Sanitizes()
        {
            string secretReplacement = "[Hidden Credential]";
            string secretString = "{ \"AzureWebJobsStorage\": \"DefaultEndpointsProtocol=https;AccountName=testAccount1;AccountKey=mykey1;EndpointSuffix=core.windows.net\", \"AnotherKey\": \"AnotherValue\" }";
            string sanitizedString = $"{{ \"AzureWebJobsStorage\": \"{secretReplacement}\", \"AnotherKey\": \"AnotherValue\" }}";

            string secretException = "Invalid string: \"DefaultEndpointsProtocol=https;AccountName=testaccount;AccountKey=testkey;BlobEndpoint=https://testaccount.blob.core.windows.net/;QueueEndpoint=https://testaccount.queue.core.windows.net/;TableEndpoint=https://testaccount.table.core.windows.net/;FileEndpoint=https://testaccount.file.core.windows.net/;\"";
            string sanitizedDetails = $"System.InvalidOperationException : Invalid string: \"{secretReplacement}\"";
            string sanitizedExceptionMessage = $"Invalid string: \"{secretReplacement}\"";

            string eventName = string.Empty;
            string functionInvocationId = string.Empty;
            string activityId = string.Empty;

            Exception ex = new InvalidOperationException(secretException);

            _mockEventGenerator.Setup(p => p.LogFunctionTraceEvent(LogLevel.Error, _subscriptionId, _websiteName, _functionName, eventName, _category, sanitizedDetails, sanitizedString, ex.GetType().ToString(), sanitizedExceptionMessage, functionInvocationId, _hostInstanceId, activityId));

            _logger.LogError(ex, secretString);

            _mockEventGenerator.VerifyAll();
        }

        [Fact]
        public void Log_Ignores_FunctionUserCategory()
        {
            // Create a logger with the Function.{FunctionName}.User category, which is what determines user logs.
            ILogger logger = new SystemLogger(Guid.NewGuid().ToString(), LogCategories.CreateFunctionUserCategory(_functionName), _mockEventGenerator.Object, new TestEnvironment());
            logger.LogDebug("TestMessage");

            // Make sure it's never been called.
            _mockEventGenerator.Verify(p => p.LogFunctionTraceEvent(It.IsAny<LogLevel>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), string.Empty, string.Empty, string.Empty, string.Empty, string.Empty), Times.Never);
        }

        [Fact]
        public void Log_Ignores_UserLogStateValue()
        {
            var logState = new Dictionary<string, object>
            {
                [ScriptConstants.LogPropertyIsUserLogKey] = true
            };

            // Try to pass the key via the state in two different ways. Neither should be logged.
            _logger.Log(LogLevel.Debug, 0, logState, null, (s, e) => "TestMessage");
            _logger.LogDebug($"{{{ScriptConstants.LogPropertyIsUserLogKey}}}", true);

            // Make sure it's never been called.
            _mockEventGenerator.Verify(p => p.LogFunctionTraceEvent(It.IsAny<LogLevel>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), string.Empty, string.Empty, string.Empty, string.Empty, string.Empty), Times.Never);
        }

        [Fact]
        public async Task Log_Ignores_DeferredLogs()
        {
            // Use the DeferredLoggerService to write a log, which marks logs as "Deferred".
            // The SystemLogger ignores these as they are logged directly by the WebHost.

            // Produce logs from the WebHost.
            var provider = new DeferredLoggerProvider(new ScriptWebHostEnvironment(new TestEnvironment()));
            var webLogger = provider.CreateLogger("FromWebHost");

            webLogger.LogInformation("1");

            provider.Dispose();

            // Now consume those and make sure they don't flow through to the SystemLogger.
            var testProvider = new TestLoggerProvider();
            var options = new OptionsWrapper<ScriptJobHostOptions>(new ScriptJobHostOptions());
            var eventGenerator = new MockEventGenerator();
            var systemProvider = new SystemLoggerProvider(options, eventGenerator, new TestEnvironment());
            var factory = new LoggerFactory();
            factory.AddProvider(testProvider);
            factory.AddProvider(systemProvider);

            var service = new DeferredLoggerService(provider, factory);
            await service.StartAsync(CancellationToken.None);

            // This will complete when the buffer has been emptied.
            await provider.LogBuffer.Completion;

            await service.StopAsync(CancellationToken.None);

            // Make sure that everything is actually wired up. This one should show up in both.
            var jobLogger = factory.CreateLogger("FromJobHost");
            jobLogger.LogInformation("2");

            // The TestLogger sees both; SystemLogger only sees "2"
            var testLogs = testProvider.GetAllLogMessages();
            Assert.Equal(2, testLogs.Count);
            Assert.Equal("1", testLogs[0].FormattedMessage);
            Assert.Equal("2", testLogs[1].FormattedMessage);
            Assert.Equal("2", eventGenerator.Summaries.Single());
        }

        // Simplify tracking of logs.
        private class MockEventGenerator : IEventGenerator
        {
            private readonly IList<string> _summaries = new List<string>();

            public IEnumerable<string> Summaries => _summaries;

            public void LogFunctionDetailsEvent(string siteName, string functionName, string inputBindings, string outputBindings, string scriptType, bool isDisabled)
            {
                throw new NotImplementedException();
            }

            public void LogFunctionExecutionAggregateEvent(string siteName, string functionName, long executionTimeInMs, long functionStartedCount, long functionCompletedCount, long functionFailedCount)
            {
                throw new NotImplementedException();
            }

            public void LogFunctionExecutionEvent(string executionId, string siteName, int concurrency, string functionName, string invocationId, string executionStage, long executionTimeSpan, bool success)
            {
                throw new NotImplementedException();
            }

            public void LogFunctionMetricEvent(string subscriptionId, string appName, string functionName, string eventName, long average, long minimum, long maximum, long count, DateTime eventTimestamp)
            {
                throw new NotImplementedException();
            }

            public void LogFunctionTraceEvent(LogLevel level, string subscriptionId, string appName, string functionName, string eventName, string source, string details, string summary, string exceptionType, string exceptionMessage, string functionInvocationId, string hostInstanceId, string activityId)
            {
                _summaries.Add(summary);
            }
        }
    }
}