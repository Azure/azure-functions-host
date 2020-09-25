// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Eventing
{
    public class DiagnosticLoggerTests
    {
        private static readonly string _functionName = "TestFunction";
        private static readonly string _regionName = "West US";
        private static readonly string _websiteHostName = "functionstest.azurewebsites.net";
        private static readonly string _subscriptionId = "e3235165-1600-4819-85f0-2ab362e909e4";
        private static readonly string _roleInstance = "e777fde04dea4eb931d5e5f06e65b4fdf5b375aed60af41dd7b491cf5792e01b";
        private static readonly int _processId = Process.GetCurrentProcess().Id;
        private readonly string _hostInstanceId = Guid.NewGuid().ToString();

        private readonly AzureMonitorDiagnosticLogger _logger;
        private readonly Mock<IEventGenerator> _mockEventGenerator;
        private readonly IEnvironment _environment = new TestEnvironment();
        private readonly TestOptionsMonitor<AppServiceOptions> _appServiceOptionsWrapper;
        private readonly string _category = LogCategories.CreateFunctionCategory(_functionName);
        private readonly HostNameProvider _hostNameProvider;

        public DiagnosticLoggerTests()
        {
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteOwnerName, $"{_subscriptionId}+westuswebspace");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHostName, _websiteHostName);
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.RegionName, _regionName);
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteInstanceId, _roleInstance);

            _mockEventGenerator = new Mock<IEventGenerator>(MockBehavior.Strict);

            var appServiceOptions = new AppServiceOptions
            {
                AppName = "TestApp",
                SlotName = "Production",
                SubscriptionId = "abc123",
                RuntimeSiteName = "TestApp_Runtime"
            };

            _appServiceOptionsWrapper = new TestOptionsMonitor<AppServiceOptions>(appServiceOptions);

            _category = LogCategories.CreateFunctionCategory(_functionName);
            var loggerProvider = new TestLoggerProvider();
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(loggerProvider);
            _hostNameProvider = new HostNameProvider(_environment);
            _logger = new AzureMonitorDiagnosticLogger(_category, _hostInstanceId, _mockEventGenerator.Object, _environment, new LoggerExternalScopeProvider(), _hostNameProvider, _appServiceOptionsWrapper);
        }

        [Fact]
        public void Log_EmitsExpectedEvent()
        {
            string message = "TestMessage";
            string functionInvocationId = Guid.NewGuid().ToString();
            string activityId = Guid.NewGuid().ToString();

            string properties = null;
            _mockEventGenerator.Setup(p => p.LogAzureMonitorDiagnosticLogEvent(LogLevel.Debug, _websiteHostName, AzureMonitorDiagnosticLogger.AzureMonitorOperationName, AzureMonitorDiagnosticLogger.AzureMonitorCategoryName, _regionName, It.IsAny<string>()))
                  .Callback<LogLevel, string, string, string, string, string>((t, r, o, c, l, p) =>
                  {
                      // Store off the properties for later validation
                      properties = p;
                  });

            using (CreateScope(activityId: activityId, functionName: _functionName, functionInvocationId: functionInvocationId))
            {
                _logger.LogDebug(new EventId(123, "TestEvent"), message);
            }

            _mockEventGenerator.VerifyAll();

            JObject actual = JObject.Parse(properties);

            var level = LogLevel.Debug;
            JObject expected = JObject.FromObject(new
            {
                appName = _appServiceOptionsWrapper.CurrentValue.AppName,
                roleInstance = _roleInstance,
                message,
                category = _category,
                hostVersion = ScriptHost.Version,
                functionInvocationId,
                functionName = _functionName,
                hostInstanceId = _hostInstanceId,
                activityId,
                level = level.ToString(),
                levelId = (int)level,
                processId = _processId,
                eventId = 123,
                eventName = "TestEvent"
            });

            Assert.True(JToken.DeepEquals(actual, expected), $"Actual: {actual.ToString()}{Environment.NewLine}Expected: {expected.ToString()}");
        }

        [Fact]
        public void Log_Error_EmitsExpectedEvent()
        {
            Exception ex = new Exception("Kaboom");

            string message = "TestMessage";
            string functionInvocationId = Guid.NewGuid().ToString();
            string activityId = Guid.NewGuid().ToString();

            string properties = null;
            _mockEventGenerator.Setup(p => p.LogAzureMonitorDiagnosticLogEvent(LogLevel.Error, _websiteHostName, AzureMonitorDiagnosticLogger.AzureMonitorOperationName, AzureMonitorDiagnosticLogger.AzureMonitorCategoryName, _regionName, It.IsAny<string>()))
                  .Callback<LogLevel, string, string, string, string, string>((t, r, o, c, l, p) =>
                  {
                      // Store off the properties for later validation
                      properties = p;
                  });

            // use no scope
            using (CreateScope())
            {
                _logger.LogError(ex, message);
            }

            _mockEventGenerator.VerifyAll();

            var level = LogLevel.Error;

            JObject actual = JObject.Parse(properties);
            JObject expected = JObject.FromObject(new
            {
                appName = _appServiceOptionsWrapper.CurrentValue.AppName,
                roleInstance = _roleInstance,
                exceptionType = ex.GetType().ToString(),
                exceptionMessage = ex.Message,
                exceptionDetails = ex.ToFormattedString(),
                message,
                category = _category,
                hostInstanceId = _hostInstanceId,
                hostVersion = ScriptHost.Version,
                level = level.ToString(),
                levelId = (int)level,
                processId = _processId
            });

            Assert.True(JToken.DeepEquals(actual, expected), $"Actual: {actual.ToString()}{Environment.NewLine}Expected: {expected.ToString()}");
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

            string functionInvocationId = Guid.NewGuid().ToString();
            Exception ex = new InvalidOperationException(secretException);

            string properties = null;
            _mockEventGenerator.Setup(p => p.LogAzureMonitorDiagnosticLogEvent(LogLevel.Error, _websiteHostName, AzureMonitorDiagnosticLogger.AzureMonitorOperationName, AzureMonitorDiagnosticLogger.AzureMonitorCategoryName, _regionName, It.IsAny<string>()))
                  .Callback<LogLevel, string, string, string, string, string>((t, r, o, c, l, p) =>
                  {
                      // Store off the properties for later validation
                      properties = p;
                  });

            using (CreateScope(functionName: _functionName, functionInvocationId: functionInvocationId))
            {
                _logger.LogError(ex, secretString);
            }

            _mockEventGenerator.VerifyAll();

            var level = LogLevel.Error;

            JObject actual = JObject.Parse(properties);
            JObject expected = JObject.FromObject(new
            {
                appName = _appServiceOptionsWrapper.CurrentValue.AppName,
                roleInstance = _roleInstance,
                category = _category,
                exceptionDetails = sanitizedDetails,
                exceptionMessage = sanitizedExceptionMessage,
                exceptionType = ex.GetType().ToString(),
                functionInvocationId,
                functionName = _functionName,
                hostInstanceId = _hostInstanceId,
                hostVersion = ScriptHost.Version,
                level = level.ToString(),
                levelId = (int)level,
                message = sanitizedString,
                processId = _processId
            });

            Assert.True(JToken.DeepEquals(actual, expected), $"Actual: {actual.ToString()}{Environment.NewLine}Expected: {expected.ToString()}");
        }

        [Fact]
        public void Log_DisabledIfPlaceholder()
        {
            string message = "TestMessage";
            string functionInvocationId = Guid.NewGuid().ToString();
            string activityId = Guid.NewGuid().ToString();

            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");

            _logger.LogInformation(message);

            Assert.False(_logger.IsEnabled(LogLevel.Information));
            _mockEventGenerator.Verify(m => m.LogAzureMonitorDiagnosticLogEvent(It.IsAny<LogLevel>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public void Log_DisabledIfNoSiteName()
        {
            string message = "TestMessage";
            string functionInvocationId = Guid.NewGuid().ToString();
            string activityId = Guid.NewGuid().ToString();
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHostName, null);

            // Recreate the logger was we cache the site name in the constructor
            ILogger logger = new AzureMonitorDiagnosticLogger(_category, _hostInstanceId, _mockEventGenerator.Object, _environment, new LoggerExternalScopeProvider(), _hostNameProvider, _appServiceOptionsWrapper);

            logger.LogInformation(message);

            Assert.False(logger.IsEnabled(LogLevel.Information));
            _mockEventGenerator.Verify(m => m.LogAzureMonitorDiagnosticLogEvent(It.IsAny<LogLevel>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        // Creates a scope based on the non-null values passed in. Allows us to test various permutations and make sure that the logger handles them.
        private IDisposable CreateScope(string functionName = null, string activityId = null, string functionInvocationId = null)
        {
            var scope = new Dictionary<string, object>();

            if (functionName != null)
            {
                scope[ScopeKeys.FunctionName] = functionName;
            }

            if (activityId != null)
            {
                scope[ScriptConstants.LogPropertyActivityIdKey] = activityId;
            }

            if (functionInvocationId != null)
            {
                scope[ScopeKeys.FunctionInvocationId] = functionInvocationId;
            }

            return _logger?.BeginScope(scope);
        }
    }
}
