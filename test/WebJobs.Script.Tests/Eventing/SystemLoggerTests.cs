// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class SystemLoggerTests : IDisposable
    {
        private readonly SystemLogger _logger;
        private readonly Mock<IEventGenerator> _mockEventGenerator;
        private readonly string _websiteName;
        private readonly string _subscriptionId;
        private readonly ScriptSettingsManager _settingsManager;
        private readonly string _category;
        private readonly string _functionName = "TestFunction";

        public SystemLoggerTests()
        {
            _settingsManager = ScriptSettingsManager.Instance;

            _subscriptionId = "e3235165-1600-4819-85f0-2ab362e909e4";
            _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteOwnerName, $"{_subscriptionId}+westuswebspace");

            _websiteName = "functionstest";
            _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteName, _websiteName);

            _mockEventGenerator = new Mock<IEventGenerator>(MockBehavior.Strict);

            _category = LogCategories.CreateFunctionCategory(_functionName);
            _logger = new SystemLogger(_category, _mockEventGenerator.Object, _settingsManager);
        }

        [Fact]
        public void Trace_Verbose_EmitsExpectedEvent()
        {
            string eventName = string.Empty;
            string details = string.Empty;
            string message = "TestMessage";

            _mockEventGenerator.Setup(p => p.LogFunctionTraceEvent(LogLevel.Debug, _subscriptionId, _websiteName, _functionName, eventName, _category, details, message, string.Empty, string.Empty));

            _logger.LogDebug(message);

            _mockEventGenerator.VerifyAll();
        }

        [Fact]
        public void Trace_Error_EmitsExpectedEvent()
        {
            string eventName = string.Empty;
            string message = "TestMessage";

            Exception ex = new Exception("Kaboom");

            _mockEventGenerator.Setup(p => p.LogFunctionTraceEvent(LogLevel.Error, _subscriptionId, _websiteName, _functionName, eventName, _category, ex.ToFormattedString(), message, ex.GetType().ToString(), ex.Message));

            _logger.LogError(ex, message);

            _mockEventGenerator.VerifyAll();
        }

        [Fact]
        public void Trace_Sanitizes()
        {
            string secretReplacement = "[Hidden Credential]";
            string secretString = "{ \"AzureWebJobsStorage\": \"DefaultEndpointsProtocol=https;AccountName=testAccount1;AccountKey=mykey1;EndpointSuffix=core.windows.net\", \"AnotherKey\": \"AnotherValue\" }";
            string sanitizedString = $"{{ \"AzureWebJobsStorage\": \"{secretReplacement}\", \"AnotherKey\": \"AnotherValue\" }}";

            string secretException = "Invalid string: \"DefaultEndpointsProtocol=https;AccountName=testaccount;AccountKey=testkey;BlobEndpoint=https://testaccount.blob.core.windows.net/;QueueEndpoint=https://testaccount.queue.core.windows.net/;TableEndpoint=https://testaccount.table.core.windows.net/;FileEndpoint=https://testaccount.file.core.windows.net/;\"";
            string sanitizedDetails = $"System.InvalidOperationException : Invalid string: \"{secretReplacement}\"";
            string sanitizedExceptionMessage = $"Invalid string: \"{secretReplacement}\"";

            string eventName = string.Empty;

            Exception ex = new InvalidOperationException(secretException);

            _mockEventGenerator.Setup(p => p.LogFunctionTraceEvent(LogLevel.Error, _subscriptionId, _websiteName, _functionName, eventName, _category, sanitizedDetails, sanitizedString, ex.GetType().ToString(), sanitizedExceptionMessage));

            _logger.LogError(ex, secretString);

            _mockEventGenerator.VerifyAll();
        }

        [Fact]
        public void Trace_Ignores_FunctionUserCategory()
        {
            // Create a logger with the Function.{FunctionName}.User category, which is what determines user logs.
            ILogger logger = new SystemLogger(LogCategories.CreateFunctionUserCategory(_functionName), _mockEventGenerator.Object, _settingsManager);
            logger.LogDebug("TestMessage");

            // Make sure it's never been called.
            _mockEventGenerator.Verify(p => p.LogFunctionTraceEvent(It.IsAny<LogLevel>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), string.Empty, string.Empty), Times.Never);
        }

        [Fact]
        public void Trace_Ignores_UserLogStateValue()
        {
            var logState = new Dictionary<string, object>
            {
                [ScriptConstants.LogPropertyIsUserLogKey] = true
            };

            // Try to pass the key via the state in two different ways. Neither should be logged.
            _logger.Log(LogLevel.Debug, 0, logState, null, (s, e) => "TestMessage");
            _logger.LogDebug($"{{{ScriptConstants.LogPropertyIsUserLogKey}}}", true);

            // Make sure it's never been called.
            _mockEventGenerator.Verify(p => p.LogFunctionTraceEvent(It.IsAny<LogLevel>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), string.Empty, string.Empty), Times.Never);
        }

        public void Dispose()
        {
            _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteOwnerName, null);
            _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteName, null);
        }
    }
}