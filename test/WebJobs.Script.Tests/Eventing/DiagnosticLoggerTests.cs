// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Eventing
{
    public class DiagnosticLoggerTests
    {
        private readonly DiagnosticLogger _logger;
        private readonly Mock<IEventGenerator> _mockEventGenerator;
        private readonly string _websiteName;
        private readonly string _subscriptionId;
        private readonly string _regionName;
        private readonly string _resourceGroup;
        private readonly ScriptSettingsManager _settingsManager;
        private readonly string _category;
        private readonly string _functionName = "TestFunction";
        private readonly string _hostInstanceId;

        public DiagnosticLoggerTests()
        {
            _settingsManager = ScriptSettingsManager.Instance;

            _subscriptionId = "e3235165-1600-4819-85f0-2ab362e909e4";
            _hostInstanceId = Guid.NewGuid().ToString();
            _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteOwnerName, $"{_subscriptionId}+westuswebspace");

            _websiteName = "functionstest";
            _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteName, _websiteName);

            _regionName = "West US";
            _settingsManager.SetSetting(EnvironmentSettingNames.RegionName, _regionName);

            _resourceGroup = "testrg";
            _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteResourceGroup, _resourceGroup);

            _mockEventGenerator = new Mock<IEventGenerator>(MockBehavior.Strict);

            _category = LogCategories.CreateFunctionCategory(_functionName);
            _logger = new DiagnosticLogger(_category, _mockEventGenerator.Object, _settingsManager, (c, l) => true);
        }

        [Fact]
        public void Log_EmitsExpectedEvent()
        {
            string message = "TestMessage";
            string functionInvocationId = Guid.NewGuid().ToString();
            string activityId = Guid.NewGuid().ToString();

            string resourceId = DiagnosticLogger.GenerateResourceId(_subscriptionId, _resourceGroup, _websiteName, null);

            string properties = null;
            _mockEventGenerator.Setup(p => p.LogFunctionDiagnosticEvent(LogLevel.Debug, resourceId, DiagnosticLogger.AzureMonitorOperationName, DiagnosticLogger.AzureMonitorCategoryName, _regionName, It.IsAny<string>()))
                  .Callback<LogLevel, string, string, string, string, string>((t, r, o, c, l, p) =>
                  {
                      // Store off the properties for later validation
                      properties = p;
                  });

            using (CreateScope(hostInstanceId: _hostInstanceId, activityId: activityId, functionName: _functionName, functionInvocationId: functionInvocationId))
            {
                _logger.LogDebug(message);
            }

            _mockEventGenerator.VerifyAll();

            JObject actual = JObject.Parse(properties);
            JObject expected = JObject.FromObject(new
            {
                message,
                category = _category,
                hostVersion = ScriptHost.Version,
                functionInvocationId,
                functionName = _functionName,
                hostInstanceId = _hostInstanceId,
                activityId,
                level = "Debug"
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

            string resourceId = DiagnosticLogger.GenerateResourceId(_subscriptionId, _resourceGroup, _websiteName, null);

            string properties = null;
            _mockEventGenerator.Setup(p => p.LogFunctionDiagnosticEvent(LogLevel.Error, resourceId, DiagnosticLogger.AzureMonitorOperationName, DiagnosticLogger.AzureMonitorCategoryName, _regionName, It.IsAny<string>()))
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

            JObject actual = JObject.Parse(properties);
            JObject expected = JObject.FromObject(new
            {
                exceptionType = ex.GetType().ToString(),
                exceptionMessage = ex.Message,
                exceptionDetails = ex.ToFormattedString(),
                message,
                category = _category,
                hostVersion = ScriptHost.Version,
                level = "Error"
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
            string resourceId = DiagnosticLogger.GenerateResourceId(_subscriptionId, _resourceGroup, _websiteName, null);

            string properties = null;
            _mockEventGenerator.Setup(p => p.LogFunctionDiagnosticEvent(LogLevel.Error, resourceId, DiagnosticLogger.AzureMonitorOperationName, DiagnosticLogger.AzureMonitorCategoryName, _regionName, It.IsAny<string>()))
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

            JObject actual = JObject.Parse(properties);
            JObject expected = JObject.FromObject(new
            {
                exceptionType = ex.GetType().ToString(),
                exceptionMessage = sanitizedExceptionMessage,
                exceptionDetails = sanitizedDetails,
                message = sanitizedString,
                category = _category,
                functionName = _functionName,
                functionInvocationId,
                hostVersion = ScriptHost.Version,
                level = "Error"
            });

            Assert.True(JToken.DeepEquals(actual, expected), $"Actual: {actual.ToString()}{Environment.NewLine}Expected: {expected.ToString()}");
        }

        // Creates a scope based on the non-null values passed in. Allows us to test various permutations and make sure that the logger handles them.
        private IDisposable CreateScope(string functionName = null, string hostInstanceId = null, string activityId = null, string functionInvocationId = null)
        {
            var scope = new Dictionary<string, object>();

            if (functionName != null)
            {
                scope[ScopeKeys.FunctionName] = functionName;
            }

            if (hostInstanceId != null)
            {
                scope[ScopeKeys.HostInstanceId] = hostInstanceId;
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

        public void Dispose()
        {
            _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteOwnerName, null);
            _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteName, null);
            _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteResourceGroup, null);
            _settingsManager.SetSetting(EnvironmentSettingNames.RegionName, null);
        }
    }
}
