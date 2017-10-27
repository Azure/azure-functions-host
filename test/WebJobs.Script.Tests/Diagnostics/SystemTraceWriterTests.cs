// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#if SYSTEMTRACEWRITER
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

            TraceEvent traceEvent = new TraceEvent(TraceLevel.Verbose, "TestMessage", "TestSource");

            traceEvent.Properties.Add(ScriptConstants.TracePropertyEventNameKey, eventName);
            traceEvent.Properties.Add(ScriptConstants.TracePropertyFunctionNameKey, functionName);
            traceEvent.Properties.Add(ScriptConstants.TracePropertyEventDetailsKey, details);

            _mockEventGenerator.Setup(p => p.LogFunctionTraceEvent(TraceLevel.Verbose, _subscriptionId, _websiteName, functionName, eventName, traceEvent.Source, details, traceEvent.Message));

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

            _mockEventGenerator.Setup(p => p.LogFunctionTraceEvent(TraceLevel.Error, _subscriptionId, _websiteName, functionName, eventName, traceEvent.Source, ex.ToFormattedString(), traceEvent.Message));

            _traceWriter.Trace(traceEvent);

            _mockEventGenerator.VerifyAll();
        }

        [Fact]
        public void Trace_Sanitizes()
        {
            string secretReplacement = "[Hidden Credential]";
            string secretString = "{ \"AzureWebJobsStorage\": \"DefaultEndpointsProtocol=https;AccountName=testAccount1;AccountKey=mykey1;EndpointSuffix=core.windows.net\", \"AnotherKey\": \"AnotherValue\" }";
            string sanitizedString = $"{{ \"AzureWebJobsStorage\": \"{secretReplacement}\", \"AnotherKey\": \"AnotherValue\" }}";

            string secretException = "Invalid string: \"DefaultEndpointsProtocol=https;AccountName=testaccount;AccountKey=testkey;BlobEndpoint=https://testaccount.blob.core.windows.net/;QueueEndpoint=https://testaccount.queue.core.windows.net/;TableEndpoint=https://testaccount.table.core.windows.net/;FileEndpoint=https://testaccount.file.core.windows.net/;\"";
            string sanitizedException = $"System.InvalidOperationException : Invalid string: \"{secretReplacement}\"";

            string functionName = "TestFunction";
            string eventName = "TestEvent";

            Exception ex = new InvalidOperationException(secretException);

            TraceEvent traceEvent = new TraceEvent(TraceLevel.Error, secretString, "TestSource", ex);

            traceEvent.Properties.Add(ScriptConstants.TracePropertyEventNameKey, eventName);
            traceEvent.Properties.Add(ScriptConstants.TracePropertyFunctionNameKey, functionName);

            _mockEventGenerator.Setup(p => p.LogFunctionTraceEvent(TraceLevel.Error, _subscriptionId, _websiteName, functionName, eventName, traceEvent.Source, sanitizedException, sanitizedString));

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
#endif