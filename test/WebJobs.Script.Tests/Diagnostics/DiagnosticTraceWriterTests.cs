// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class DiagnosticTraceWriterTests : IDisposable
    {
        private readonly DiagnosticTraceWriter _traceWriter;
        private readonly Mock<IEventGenerator> _mockEventGenerator;
        private readonly string _websiteName;
        private readonly string _subscriptionId;
        private readonly string _regionName;
        private readonly string _resourceGroup;
        private readonly ScriptSettingsManager _settingsManager;

        public DiagnosticTraceWriterTests()
        {
            _settingsManager = new ScriptSettingsManager();

            _subscriptionId = "e3235165-1600-4819-85f0-2ab362e909e4";
            _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteOwnerName, $"{_subscriptionId}+westuswebspace");

            _websiteName = "functionstest";
            _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteName, _websiteName);

            _regionName = "West US";
            _settingsManager.SetSetting(EnvironmentSettingNames.RegionName, _regionName);

            _resourceGroup = "testrg";
            _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteResourceGroup, _resourceGroup);

            _mockEventGenerator = new Mock<IEventGenerator>(MockBehavior.Strict);
            _traceWriter = new DiagnosticTraceWriter(_mockEventGenerator.Object, _settingsManager, TraceLevel.Verbose);
        }

        [Fact]
        public void Trace_Verbose_EmitsExpectedEvent()
        {
            string functionName = "TestFunction";
            string functionInvocationId = Guid.NewGuid().ToString();
            string hostInstanceId = Guid.NewGuid().ToString();
            string activityId = Guid.NewGuid().ToString();
            string message = "TestMessage";
            string source = "TestSource";

            TraceEvent traceEvent = new TraceEvent(TraceLevel.Verbose, message, source);

            traceEvent.Properties.Add(ScriptConstants.TracePropertyFunctionNameKey, functionName);
            traceEvent.Properties.Add(ScriptConstants.TracePropertyFunctionInvocationIdKey, functionInvocationId);
            traceEvent.Properties.Add(ScriptConstants.TracePropertyInstanceIdKey, hostInstanceId);
            traceEvent.Properties.Add(ScriptConstants.TracePropertyActivityIdKey, activityId);

            string resourceId = DiagnosticTraceWriter.GenerateResourceId(_subscriptionId, _resourceGroup, _websiteName, null);

            string properties = null;
            _mockEventGenerator
                .Setup(p => p.LogFunctionDiagnosticEvent(TraceLevel.Verbose, resourceId, DiagnosticTraceWriter.AzureMonitorOperationName, DiagnosticTraceWriter.AzureMonitorCategoryName, _regionName, It.IsAny<string>()))
                .Callback<TraceLevel, string, string, string, string, string>((t, r, o, c, l, p) =>
                {
                    // Store off the properties for later validation
                    properties = p;
                });

            _traceWriter.Trace(traceEvent);

            _mockEventGenerator.VerifyAll();

            JObject actual = JObject.Parse(properties);
            JObject expected = JObject.FromObject(new
            {
                message,
                category = source,
                activityId,
                functionName,
                functionInvocationId,
                hostInstanceId,
                hostVersion = ScriptHost.Version,
                level = "Verbose"
            });

            Assert.True(JToken.DeepEquals(actual, expected), $"Actual: {actual.ToString()}{Environment.NewLine}Expected: {expected.ToString()}");
        }

        [Fact]
        public void Trace_Error_EmitsExpectedEvent()
        {
            string functionName = "TestFunction";
            string eventName = "TestEvent";
            string functionInvocationId = Guid.NewGuid().ToString();
            string hostInstanceId = Guid.NewGuid().ToString();
            string hostId = Guid.NewGuid().ToString();
            string activityId = Guid.NewGuid().ToString();
            string message = "TestMessage";
            string source = "TestSource";

            Exception ex = new Exception("Kaboom");

            TraceEvent traceEvent = new TraceEvent(TraceLevel.Error, message, source, ex);

            traceEvent.Properties.Add(ScriptConstants.TracePropertyEventNameKey, eventName);
            traceEvent.Properties.Add(ScriptConstants.TracePropertyFunctionNameKey, functionName);
            traceEvent.Properties.Add(ScriptConstants.TracePropertyFunctionInvocationIdKey, functionInvocationId);
            traceEvent.Properties.Add(ScriptConstants.TracePropertyInstanceIdKey, hostInstanceId);
            traceEvent.Properties.Add(ScriptConstants.TracePropertyActivityIdKey, activityId);

            string resourceId = DiagnosticTraceWriter.GenerateResourceId(_subscriptionId, _resourceGroup, _websiteName, null);

            string properties = null;
            _mockEventGenerator
                .Setup(p => p.LogFunctionDiagnosticEvent(TraceLevel.Error, resourceId, DiagnosticTraceWriter.AzureMonitorOperationName, DiagnosticTraceWriter.AzureMonitorCategoryName, _regionName, It.IsAny<string>()))
                .Callback<TraceLevel, string, string, string, string, string>((t, r, o, c, l, p) =>
                {
                    // Store off the properties for later validation
                    properties = p;
                });

            _traceWriter.Trace(traceEvent);

            _mockEventGenerator.VerifyAll();

            JObject actual = JObject.Parse(properties);
            JObject expected = JObject.FromObject(new
            {
                exceptionType = ex.GetType().ToString(),
                exceptionMessage = ex.Message,
                exceptionDetails = ex.ToFormattedString(),
                message,
                category = source,
                hostVersion = ScriptHost.Version,
                functionInvocationId,
                functionName,
                hostInstanceId,
                activityId,
                level = "Error"
            });

            Assert.True(JToken.DeepEquals(actual, expected), $"Actual: {actual.ToString()}{Environment.NewLine}Expected: {expected.ToString()}");
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
