// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Diagnostics.Tracing;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    internal class EventGenerator : IEventGenerator
    {
        private const string EventTimestamp = "MM/dd/yyyy hh:mm:ss.fff tt";

        public void LogFunctionTraceEvent(TraceLevel level, string subscriptionId, string appName, string functionName, string eventName, string source, string details, string summary, Exception exception = null)
        {
            string eventTimestamp = DateTime.UtcNow.ToString(EventTimestamp);
            switch (level)
            {
                case TraceLevel.Verbose:
                    FunctionsEventSource.Instance.RaiseFunctionsEventVerbose(subscriptionId, appName, functionName, eventName, source, details, summary, ScriptHost.Version, eventTimestamp);
                    break;
                case TraceLevel.Info:
                    FunctionsEventSource.Instance.RaiseFunctionsEventInfo(subscriptionId, appName, functionName, eventName, source, details, summary, ScriptHost.Version, eventTimestamp);
                    break;
                case TraceLevel.Warning:
                    FunctionsEventSource.Instance.RaiseFunctionsEventWarning(subscriptionId, appName, functionName, eventName, source, details, summary, ScriptHost.Version, eventTimestamp);
                    break;
                case TraceLevel.Error:
                    if (string.IsNullOrEmpty(details) && exception != null)
                    {
                        details = exception.ToString();
                    }
                    FunctionsEventSource.Instance.RaiseFunctionsEventError(subscriptionId, appName, functionName, eventName, source, details, summary, ScriptHost.Version, eventTimestamp);
                    break;
            }
        }

        public void LogFunctionMetricEvent(string subscriptionId, string appName, string eventName, long average, long minimum, long maximum, long count, DateTime eventTimestamp)
        {
            FunctionsEventSource.Instance.LogFunctionMetricEvent(subscriptionId, appName, eventName, average, minimum, maximum, count, ScriptHost.Version, eventTimestamp.ToString(EventTimestamp));
        }

        public void LogFunctionExecutionAggregateEvent(string siteName, string functionName, long executionTimeInMs, long functionStartedCount, long functionCompletedCount, long functionFailedCount)
        {
            FunctionsEventSource.Instance.LogFunctionExecutionAggregateEvent(siteName, functionName, (ulong)executionTimeInMs, (ulong)functionStartedCount, (ulong)functionCompletedCount, (ulong)functionFailedCount);
        }

        public void LogFunctionDetailsEvent(string siteName, string functionName, string inputBindings, string outputBindings, string scriptType, bool isDisabled)
        {
            FunctionsEventSource.Instance.LogFunctionDetailsEvent(siteName, functionName, inputBindings, outputBindings, scriptType, isDisabled);
        }

        public void LogFunctionExecutionEvent(string executionId, string siteName, int concurrency, string functionName, string invocationId, string executionStage, long executionTimeSpan, bool success)
        {
            FunctionsEventSource.Instance.LogFunctionExecutionEvent(executionId, siteName, concurrency, functionName, invocationId, executionStage, (ulong)executionTimeSpan, success);
        }

        [EventSource(Guid = "08D0D743-5C24-43F9-9723-98277CEA5F9B")]
        public class FunctionsEventSource : EventSource
        {
            internal static readonly FunctionsEventSource Instance = new FunctionsEventSource();

            [Event(57907, Level = EventLevel.Informational, Channel = EventChannel.Operational)]
            public void LogFunctionExecutionAggregateEvent(string siteName, string functionName, ulong executionTimeInMs, ulong functionStartedCount, ulong functionCompletedCount, ulong functionFailedCount)
            {
                if (IsEnabled())
                {
                    WriteEvent(57907, siteName, functionName, executionTimeInMs, functionStartedCount, functionCompletedCount, functionFailedCount);
                }
            }

            [Event(57908, Level = EventLevel.Informational, Channel = EventChannel.Operational)]
            public void LogFunctionDetailsEvent(string siteName, string functionName, string inputBindings, string outputBindings, string scriptType, bool isDisabled)
            {
                if (IsEnabled())
                {
                    WriteEvent(57908, siteName, functionName, inputBindings, outputBindings, scriptType, isDisabled);
                }
            }

            [Event(57909, Level = EventLevel.Informational, Channel = EventChannel.Operational)]
            public void LogFunctionExecutionEvent(string executionId, string siteName, int concurrency, string functionName, string invocationId, string executionStage, ulong executionTimeSpan, bool success)
            {
                if (IsEnabled())
                {
                    WriteEvent(57909, executionId, siteName, concurrency, functionName, invocationId, executionStage, executionTimeSpan, success);
                }
            }

            [Event(65520, Level = EventLevel.Verbose, Channel = EventChannel.Operational)]
            public void RaiseFunctionsEventVerbose(string subscriptionId, string appName, string functionName, string eventName, string source, string details, string summary, string hostVersion, string eventTimestamp)
            {
                if (IsEnabled())
                {
                    WriteEvent(65520, subscriptionId, appName, functionName, eventName, source, details, summary, hostVersion, eventTimestamp);
                }
            }

            [Event(65521, Level = EventLevel.Informational, Channel = EventChannel.Operational)]
            public void RaiseFunctionsEventInfo(string subscriptionId, string appName, string functionName, string eventName, string source, string details, string summary, string hostVersion, string eventTimestamp)
            {
                if (IsEnabled())
                {
                    WriteEvent(65521, subscriptionId, appName, functionName, eventName, source, details, summary, hostVersion, eventTimestamp);
                }
            }

            [Event(65522, Level = EventLevel.Warning, Channel = EventChannel.Operational)]
            public void RaiseFunctionsEventWarning(string subscriptionId, string appName, string functionName, string eventName, string source, string details, string summary, string hostVersion, string eventTimestamp)
            {
                if (IsEnabled())
                {
                    WriteEvent(65522, subscriptionId, appName, functionName, eventName, source, details, summary, hostVersion, eventTimestamp);
                }
            }

            [Event(65523, Level = EventLevel.Error, Channel = EventChannel.Operational)]
            public void RaiseFunctionsEventError(string subscriptionId, string appName, string functionName, string eventName, string source, string details, string summary, string hostVersion, string eventTimestamp)
            {
                if (IsEnabled())
                {
                    WriteEvent(65523, subscriptionId, appName, functionName, eventName, source, details, summary, hostVersion, eventTimestamp);
                }
            }

            [Event(65524, Level = EventLevel.Informational, Channel = EventChannel.Operational)]
            public void LogFunctionMetricEvent(string subscriptionId, string appName, string eventName, long average, long minimum, long maximum, long count, string hostVersion, string eventTimestamp)
            {
                if (IsEnabled())
                {
                    WriteEvent(65524, subscriptionId, appName, eventName, average, minimum, maximum, count, hostVersion, eventTimestamp);
                }
            }
        }
    }
}
