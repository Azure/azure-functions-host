// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class SystemTraceWriterTests : IDisposable
    {
        private readonly SystemTraceWriter _traceWriter;
        private readonly Mock<IEventGenerator> _mockEventGenerator;
        private readonly string _websiteName;
        private readonly string _subscriptionId;
        private readonly ScriptSettingsManager _settingsManager;

        public SystemTraceWriterTests()
        {
            _settingsManager = new ScriptSettingsManager();

            _subscriptionId = "e3235165-1600-4819-85f0-2ab362e909e4";
            _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteOwnerName, $"{_subscriptionId}+westuswebspace");

            _websiteName = "functionstest";
            _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteName, _websiteName);

            _mockEventGenerator = new Mock<IEventGenerator>(MockBehavior.Strict);
            _traceWriter = new SystemTraceWriter(_mockEventGenerator.Object, _settingsManager, TraceLevel.Verbose);
        }

        [Fact]
        public void Trace_Verbose_EmitsExpectedEvent()
        {
            string functionName = "TestFunction";
            string eventName = "TestEvent";
            string details = "TestDetails";
            string functionInvocationId = Guid.NewGuid().ToString();
            string hostInstanceId = Guid.NewGuid().ToString();
            string hostId = Guid.NewGuid().ToString();
            string activityId = Guid.NewGuid().ToString();

            TraceEvent traceEvent = new TraceEvent(TraceLevel.Verbose, "TestMessage", "TestSource");

            traceEvent.Properties.Add(ScriptConstants.TracePropertyEventNameKey, eventName);
            traceEvent.Properties.Add(ScriptConstants.TracePropertyFunctionNameKey, functionName);
            traceEvent.Properties.Add(ScriptConstants.TracePropertyEventDetailsKey, details);
            traceEvent.Properties.Add(ScriptConstants.TracePropertyFunctionInvocationIdKey, functionInvocationId);
            traceEvent.Properties.Add(ScriptConstants.TracePropertyInstanceIdKey, hostInstanceId);
            traceEvent.Properties.Add(ScriptConstants.TracePropertyActivityIdKey, activityId);

            _mockEventGenerator.Setup(p => p.LogFunctionTraceEvent(TraceLevel.Verbose, _subscriptionId, _websiteName, functionName, eventName, traceEvent.Source, details, traceEvent.Message, string.Empty, string.Empty, functionInvocationId, hostInstanceId, activityId));

            _traceWriter.Trace(traceEvent);

            _mockEventGenerator.VerifyAll();
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

            Exception ex = new Exception("Kaboom");

            TraceEvent traceEvent = new TraceEvent(TraceLevel.Error, "TestMessage", "TestSource", ex);

            traceEvent.Properties.Add(ScriptConstants.TracePropertyEventNameKey, eventName);
            traceEvent.Properties.Add(ScriptConstants.TracePropertyFunctionNameKey, functionName);
            traceEvent.Properties.Add(ScriptConstants.TracePropertyFunctionInvocationIdKey, functionInvocationId);
            traceEvent.Properties.Add(ScriptConstants.TracePropertyInstanceIdKey, hostInstanceId);
            traceEvent.Properties.Add(ScriptConstants.TracePropertyActivityIdKey, activityId);

            _mockEventGenerator.Setup(p => p.LogFunctionTraceEvent(TraceLevel.Error, _subscriptionId, _websiteName, functionName, eventName, traceEvent.Source, ex.ToFormattedString(), traceEvent.Message, ex.GetType().ToString(), ex.Message, functionInvocationId, hostInstanceId, activityId));

            _traceWriter.Trace(traceEvent);

            _mockEventGenerator.VerifyAll();
        }

        public void Dispose()
        {
            _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteOwnerName, null);
            _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteName, null);
        }
    }
}
