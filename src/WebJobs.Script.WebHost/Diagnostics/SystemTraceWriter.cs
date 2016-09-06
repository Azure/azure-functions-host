// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using Microsoft.Azure.WebJobs.Host;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public class SystemTraceWriter : TraceWriter
    {
        private ISystemEventGenerator _eventGenerator;
        private string _appName;
        private string _subscriptionId;

        public SystemTraceWriter(TraceLevel level) : this(new SystemEventGenerator(), level)
        {
        }

        public SystemTraceWriter(ISystemEventGenerator eventGenerator, TraceLevel level) : base(level)
        {
            _appName = Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME");
            _subscriptionId = GetSubscriptionId();
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
            string summary = traceEvent.Message ?? string.Empty;

            // Apply any additional extended event info from the Properties bag
            string functionName = string.Empty;
            string eventName = string.Empty;
            string details = string.Empty;
            bool isUserTrace = false;
            if (traceEvent.Properties != null)
            {
                object value;
                if (traceEvent.Properties.TryGetValue(ScriptConstants.TracePropertyFunctionNameKey, out value) && value != null)
                {
                    functionName = value.ToString();
                }

                if (traceEvent.Properties.TryGetValue(ScriptConstants.TracePropertyEventNameKey, out value) && value != null)
                {
                    eventName = value.ToString();
                }

                if (traceEvent.Properties.TryGetValue(ScriptConstants.TracePropertyEventDetailsKey, out value) && value != null)
                {
                    details = value.ToString();
                }

                if (traceEvent.Properties.TryGetValue(ScriptConstants.TracePropertyIsUserTraceKey, out value) 
                    && value is bool && (bool)value == true)
                {
                    isUserTrace = true;
                }
            }

            if (isUserTrace)
            {
                // we don't write user traces to system logs
                return;
            }

            _eventGenerator.LogEvent(traceEvent.Level, subscriptionId, appName, functionName, eventName, source, details, summary, traceEvent.Exception);
        }

        private static string GetSubscriptionId()
        {
            string ownerName = Environment.GetEnvironmentVariable("WEBSITE_OWNER_NAME") ?? string.Empty;
            if (!string.IsNullOrEmpty(ownerName))
            {
                int idx = ownerName.IndexOf('+');
                if (idx > 0)
                {
                    return ownerName.Substring(0, idx);
                }
            }

            return null;
        }
    }
}