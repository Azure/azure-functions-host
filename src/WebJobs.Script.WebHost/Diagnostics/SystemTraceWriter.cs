// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Config;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public class SystemTraceWriter : TraceWriter
    {
        private IEventGenerator _eventGenerator;
        private ScriptSettingsManager _settingsManager;
        private string _appName;
        private string _subscriptionId;

        public SystemTraceWriter(TraceLevel level) : this(new EventGenerator(), ScriptSettingsManager.Instance, level)
        {
        }

        public SystemTraceWriter(IEventGenerator eventGenerator, ScriptSettingsManager settingsManager, TraceLevel level) : base(level)
        {
            _settingsManager = settingsManager;
            _appName = _settingsManager.AzureWebsiteUniqueSlotName;
            _subscriptionId = Utility.GetSubscriptionId();
            _eventGenerator = eventGenerator;
        }

        public override void Trace(TraceEvent traceEvent)
        {
            // Apply standard event properties
            // Note: we must be sure to default any null values to empty string
            // otherwise the ETW event will fail to be persisted (silently)
            string subscriptionId = _subscriptionId ?? string.Empty;
            string appName = _appName ?? string.Empty;
            string source = traceEvent.Source ?? string.Empty;
            string summary = Sanitizer.Sanitize(traceEvent.Message) ?? string.Empty;
            string innerExceptionType = string.Empty;
            string innerExceptionMessage = string.Empty;

            // Apply any additional extended event info from the Properties bag
            string functionName = GetTraceEventProperty(traceEvent, ScriptConstants.TracePropertyFunctionNameKey);
            string eventName = GetTraceEventProperty(traceEvent, ScriptConstants.TracePropertyEventNameKey);
            string details = Sanitizer.Sanitize(GetTraceEventProperty(traceEvent, ScriptConstants.TracePropertyEventDetailsKey));
            string functionInvocationId = GetTraceEventProperty(traceEvent, ScriptConstants.TracePropertyFunctionInvocationIdKey);
            string hostInstanceId = GetTraceEventProperty(traceEvent, ScriptConstants.TracePropertyInstanceIdKey);
            string activityId = GetTraceEventProperty(traceEvent, ScriptConstants.TracePropertyActivityIdKey);
            if (traceEvent.Properties != null)
            {
                object value;
                if (traceEvent.Properties.TryGetValue(ScriptConstants.TracePropertyIsUserTraceKey, out value)
                    && value is bool && (bool)value == true)
                {
                    // we don't write user traces to system logs
                    return;
                }
            }

            if (string.IsNullOrEmpty(details) && traceEvent.Exception != null)
            {
                if (string.IsNullOrEmpty(functionName) && traceEvent.Exception is FunctionInvocationException fex)
                {
                    functionName = string.IsNullOrEmpty(fex.MethodName) ? string.Empty : fex.MethodName.Replace("Host.Functions.", string.Empty);
                }

                (innerExceptionType, innerExceptionMessage, details) = traceEvent.GetExceptionDetails();
                innerExceptionMessage = innerExceptionMessage ?? string.Empty;
            }

            _eventGenerator.LogFunctionTraceEvent(traceEvent.Level, subscriptionId, appName, functionName, eventName, source, details, summary, innerExceptionType, innerExceptionMessage, functionInvocationId, hostInstanceId, activityId);
        }

        private static string GetTraceEventProperty(TraceEvent traceEvent, string tracePropertyFunctionNameKey)
        {
            return traceEvent.GetPropertyValueOrNull(tracePropertyFunctionNameKey) ?? string.Empty;
        }
    }
}