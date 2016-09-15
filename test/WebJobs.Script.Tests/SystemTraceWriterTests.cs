// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class SystemTraceWriterTests : IDisposable
    {
        private readonly SystemTraceWriter _traceWriter;
        private readonly Mock<ISystemEventGenerator> _mockEventGenerator;
        private readonly string _websiteName;
        private readonly string _subscriptionId;

        public SystemTraceWriterTests()
        {
            _subscriptionId = "e3235165-1600-4819-85f0-2ab362e909e4";
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteOwnerName, $"{_subscriptionId}+westuswebspace");

            _websiteName = "functionstest";
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName, _websiteName);

            _mockEventGenerator = new Mock<ISystemEventGenerator>(MockBehavior.Strict);
            _traceWriter = new SystemTraceWriter(_mockEventGenerator.Object, TraceLevel.Verbose);
        }

        [Fact]
        public void Trace_Verbose_EmitsExpectedEvent()
        {
            string functionName = "TestFunction";
            string eventName = "TestEvent";
            string details = "TestDetails";

            TraceEvent traceEvent = new TraceEvent(TraceLevel.Verbose, "TestMessage", "TestSource");

            traceEvent.Properties.Add(ScriptConstants.TracePropertyEventNameKey, eventName);
            traceEvent.Properties.Add(ScriptConstants.TracePropertyFunctionNameKey, functionName);
            traceEvent.Properties.Add(ScriptConstants.TracePropertyEventDetailsKey, details);

            _mockEventGenerator.Setup(p => p.LogEvent(TraceLevel.Verbose, _subscriptionId, _websiteName, functionName, eventName, traceEvent.Source, details, traceEvent.Message, null));

            _traceWriter.Trace(traceEvent);

            _mockEventGenerator.VerifyAll();
        }

        [Fact]
        public void Trace_Error_EmitsExpectedEvent()
        {
            string functionName = "TestFunction";
            string eventName = "TestEvent";

            Exception ex = new Exception("Kaboom");

            TraceEvent traceEvent = new TraceEvent(TraceLevel.Error, "TestMessage", "TestSource", ex);

            traceEvent.Properties.Add(ScriptConstants.TracePropertyEventNameKey, eventName);
            traceEvent.Properties.Add(ScriptConstants.TracePropertyFunctionNameKey, functionName);

            _mockEventGenerator.Setup(p => p.LogEvent(TraceLevel.Error, _subscriptionId, _websiteName, functionName, eventName, traceEvent.Source, string.Empty, traceEvent.Message, ex));

            _traceWriter.Trace(traceEvent);

            _mockEventGenerator.VerifyAll();
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteOwnerName, null);
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName, null);
        }
    }
}
