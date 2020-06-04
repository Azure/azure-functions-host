// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
        private readonly Mock<IDebugStateProvider> _debugStateProvider;
        private readonly IEnvironment _environment;
        private readonly IOptionsMonitor<AppServiceOptions> _appServiceOptions;
        private readonly TestChangeTokenSource<StandbyOptions> _changeTokenSource;
        private readonly string _slotName = "production";
        private readonly string _runtimeSiteName = "test";
        private bool _inDiagnosticMode;

        public SystemLoggerTests()
        {
            _subscriptionId = "e3235165-1600-4819-85f0-2ab362e909e4";
            _hostInstanceId = Guid.NewGuid().ToString();
            _websiteName = "functionstest";

            _environment = new TestEnvironment(new Dictionary<string, string>
            {
                { EnvironmentSettingNames.AzureWebsiteOwnerName,  $"{_subscriptionId}+westuswebspace" },
                { EnvironmentSettingNames.AzureWebsiteName,  _websiteName },
                { EnvironmentSettingNames.AzureWebsiteRuntimeSiteName, _runtimeSiteName },
                { EnvironmentSettingNames.AzureWebsiteSlotName, _slotName }
            });

            _changeTokenSource = new TestChangeTokenSource<StandbyOptions>();
            var host = new HostBuilder()
                .ConfigureServices(s =>
                {
                    s.AddSingleton<IEnvironment>(_environment);
                    s.ConfigureOptions<AppServiceOptionsSetup>();
                    s.AddSingleton<IOptionsChangeTokenSource<AppServiceOptions>, SpecializationChangeTokenSource<AppServiceOptions>>();
                    s.AddSingleton<IOptionsChangeTokenSource<StandbyOptions>>(_changeTokenSource);
                })
                .Build();

            _appServiceOptions = host.Services.GetService<IOptionsMonitor<AppServiceOptions>>();
            _mockEventGenerator = new Mock<IEventGenerator>(MockBehavior.Strict);
            _inDiagnosticMode = false;
            _category = LogCategories.CreateFunctionCategory(_functionName);
            _debugStateProvider = new Mock<IDebugStateProvider>(MockBehavior.Strict);
            _debugStateProvider.Setup(p => p.InDiagnosticMode).Returns(() => _inDiagnosticMode);

            _logger = new SystemLogger(_hostInstanceId, _category, _mockEventGenerator.Object, _environment, _debugStateProvider.Object, null, new LoggerExternalScopeProvider(), _appServiceOptions);
        }

        [Fact]
        public void Log_Trace_LogsOnlyWhenInDebugMode()
        {
            string eventName = string.Empty;
            string details = string.Empty;
            string summary = "TestMessage";
            string functionInvocationId = string.Empty;
            string activityId = string.Empty;

            _logger.LogTrace(summary);

            _inDiagnosticMode = true;
            _mockEventGenerator.Setup(p => p.LogFunctionTraceEvent(LogLevel.Trace, _subscriptionId, _websiteName, _functionName, eventName, _category, details, summary, string.Empty, string.Empty, functionInvocationId, _hostInstanceId, activityId, _runtimeSiteName, _slotName, It.IsAny<DateTime>()));
            _logger.LogTrace(summary);

            _mockEventGenerator.VerifyAll();
        }

        [Fact]
        public void Log_Verbose_EmitsExpectedEvent()
        {
            string eventName = string.Empty;
            string details = string.Empty;
            string summary = "TestMessage";
            string functionInvocationId = string.Empty;
            string activityId = string.Empty;

            _mockEventGenerator.Setup(p => p.LogFunctionTraceEvent(LogLevel.Debug, _subscriptionId, _websiteName, _functionName, eventName, _category, details, summary, string.Empty, string.Empty, functionInvocationId, _hostInstanceId, activityId, _runtimeSiteName, _slotName, It.IsAny<DateTime>()));

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

            _mockEventGenerator.Setup(p => p.LogFunctionTraceEvent(LogLevel.Debug, _subscriptionId, _websiteName, _functionName, eventName, _category, details, message, string.Empty, string.Empty, functionInvocationId, _hostInstanceId, activityId, _runtimeSiteName, _slotName, It.IsAny<DateTime>()));

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

            _mockEventGenerator.Setup(p => p.LogFunctionTraceEvent(LogLevel.Error, _subscriptionId, _websiteName, _functionName, eventName, _category, ex.ToFormattedString(), message, ex.GetType().ToString(), ex.Message, functionInvocationId, _hostInstanceId, activityId, _runtimeSiteName, _slotName, It.IsAny<DateTime>()));

            _logger.LogError(ex, message);

            _mockEventGenerator.VerifyAll();
        }

        [Fact]
        public void Log_Sanitization()
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

            Exception ex = new InvalidOperationException(Sanitizer.Sanitize(secretException));

            _mockEventGenerator.Setup(p => p.LogFunctionTraceEvent(LogLevel.Error, _subscriptionId, _websiteName, _functionName, eventName, _category, sanitizedDetails, sanitizedString, ex.GetType().ToString(), sanitizedExceptionMessage, functionInvocationId, _hostInstanceId, activityId, _runtimeSiteName, _slotName, It.IsAny<DateTime>()));

            // it's the caller's responsibility to pre-sanitize any details in the log entries
            _logger.LogError(ex, Sanitizer.Sanitize(secretString));

            _mockEventGenerator.VerifyAll();
        }

        [Fact]
        public void Log_Ignores_FunctionUserCategory()
        {
            // Create a logger with the Function.{FunctionName}.User category, which is what determines user logs.
            ILogger logger = new SystemLogger(Guid.NewGuid().ToString(), LogCategories.CreateFunctionUserCategory(_functionName), _mockEventGenerator.Object, new TestEnvironment(), _debugStateProvider.Object, null, new LoggerExternalScopeProvider(), _appServiceOptions);
            logger.LogDebug("TestMessage");

            // Make sure it's never been called.
            _mockEventGenerator.Verify(p => p.LogFunctionTraceEvent(It.IsAny<LogLevel>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, It.IsAny<DateTime>()), Times.Never);
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
            _mockEventGenerator.Verify(p => p.LogFunctionTraceEvent(It.IsAny<LogLevel>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
        }

        [Theory]
        [InlineData("functionName")]
        [InlineData(LogConstants.NameKey)]
        [InlineData(ScopeKeys.FunctionName)]
        public void Log_UsesCategoryFunctionName(string key)
        {
            var logState = new Dictionary<string, object>
            {
                [key] = "TestFunction2"
            };

            _mockEventGenerator.Setup(p => p.LogFunctionTraceEvent(LogLevel.Debug, It.IsAny<string>(), It.IsAny<string>(), "TestFunction", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()));
            _logger.Log(LogLevel.Debug, 0, logState, null, (s, e) => "TestMessage");

            _mockEventGenerator.VerifyAll();
        }

        [Theory]
        [InlineData("functionName")]
        [InlineData(LogConstants.NameKey)]
        [InlineData(ScopeKeys.FunctionName)]
        public void Log_UsesStateFunctionName_IfNoCategory(string key)
        {
            var logState = new Dictionary<string, object>
            {
                [key] = "TestFunction2"
            };

            var localLogger = new SystemLogger(_hostInstanceId, "Not.A.Function", _mockEventGenerator.Object, _environment, _debugStateProvider.Object, null, new LoggerExternalScopeProvider(), _appServiceOptions);

            _mockEventGenerator.Setup(p => p.LogFunctionTraceEvent(LogLevel.Debug, It.IsAny<string>(), It.IsAny<string>(), "TestFunction2", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()));
            localLogger.Log(LogLevel.Debug, 0, logState, null, (s, e) => "TestMessage");

            _mockEventGenerator.VerifyAll();
        }

        [Theory]
        [InlineData("functionName")]
        [InlineData(LogConstants.NameKey)]
        [InlineData(ScopeKeys.FunctionName)]
        public void Log_UsesScopeFunctionName_IfNoCategory(string key)
        {
            var logScope = new Dictionary<string, object>
            {
                [key] = "TestFunction3"
            };

            var localLogger = new SystemLogger(_hostInstanceId, "Not.A.Function", _mockEventGenerator.Object, _environment, _debugStateProvider.Object, null, new LoggerExternalScopeProvider(), _appServiceOptions);

            _mockEventGenerator.Setup(p => p.LogFunctionTraceEvent(LogLevel.Debug, It.IsAny<string>(), It.IsAny<string>(), "TestFunction3", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()));

            using (localLogger.BeginScope(logScope))
            {
                localLogger.LogDebug("TestMessage");
            }

            _mockEventGenerator.VerifyAll();
        }

        [Fact]
        public void AppEnvironment_Reset_OnSpecialization()
        {
            var testEventGenerator = new TestEventGenerator();
            var localLogger = new SystemLogger(_hostInstanceId, "Test", testEventGenerator, _environment, _debugStateProvider.Object, null, new LoggerExternalScopeProvider(), _appServiceOptions);

            localLogger.LogInformation("test");

            var evt = testEventGenerator.GetFunctionTraceEvents().Single();
            Assert.Equal(_websiteName, evt.AppName);
            Assert.Equal(_subscriptionId, evt.SubscriptionId);
            Assert.Equal("production", evt.SlotName);
            Assert.Equal("test", evt.RuntimeSiteName);

            // now update environment
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteOwnerName, $"updatedsub+westuswebspace");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName, "updatedsitename");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteRuntimeSiteName, "updatedruntimesitename");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSlotName, "updatedslot");

            _changeTokenSource.SignalChange();

            testEventGenerator.ClearEvents();
            localLogger.LogInformation("test");

            evt = testEventGenerator.GetFunctionTraceEvents().Single();
            Assert.Equal("updatedsitename-updatedslot", evt.AppName);
            Assert.Equal("updatedsub", evt.SubscriptionId);
            Assert.Equal("updatedslot", evt.SlotName);
            Assert.Equal("updatedruntimesitename", evt.RuntimeSiteName);
        }
    }
}