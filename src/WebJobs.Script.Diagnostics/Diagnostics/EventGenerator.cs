// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Tracing;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public class EventGenerator : IEventGenerator
    {
        private const string EventTimestamp = "MM/dd/yyyy hh:mm:ss.fff tt";

        private readonly string scriptHostVersion;
        public EventGenerator(string scriptHostVersion)
        {
            this.scriptHostVersion = scriptHostVersion;
        }

        public void LogFunctionTraceEvent(TraceLevel level, string subscriptionId, string appName, string functionName, string eventName, string source, string details, string summary, Exception exception = null)
        {
            string eventTimestamp = DateTime.UtcNow.ToString(EventTimestamp);
            switch (level)
            {
                case TraceLevel.Verbose:
                    FunctionsSystemLogsEventSource.Instance.RaiseFunctionsEventVerbose(subscriptionId, appName, functionName, eventName, source, details, summary, scriptHostVersion, eventTimestamp);
                    break;
                case TraceLevel.Info:
                    FunctionsSystemLogsEventSource.Instance.RaiseFunctionsEventInfo(subscriptionId, appName, functionName, eventName, source, details, summary, scriptHostVersion, eventTimestamp);
                    break;
                case TraceLevel.Warning:
                    FunctionsSystemLogsEventSource.Instance.RaiseFunctionsEventWarning(subscriptionId, appName, functionName, eventName, source, details, summary, scriptHostVersion, eventTimestamp);
                    break;
                case TraceLevel.Error:
                    if (string.IsNullOrEmpty(details) && exception != null)
                    {
                        details = exception.ToString();
                    }
                    FunctionsSystemLogsEventSource.Instance.RaiseFunctionsEventError(subscriptionId, appName, functionName, eventName, source, details, summary, scriptHostVersion, eventTimestamp);
                    break;
            }
        }

        public void LogFunctionMetricEvent(string subscriptionId, string appName, string functionName, string eventName, long average, long minimum, long maximum, long count, DateTime eventTimestamp)
        {
            FunctionsSystemLogsEventSource.Instance.LogFunctionMetricEvent(subscriptionId, appName, functionName, eventName, average, minimum, maximum, count, scriptHostVersion, eventTimestamp.ToString(EventTimestamp));
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
        }

        [EventSource(Guid = "a7044dd6-c8ef-4980-858c-942d972b6250")]
        [SuppressMessage("Microsoft.StyleCop.CSharp.NamingRules", "SA1306:FieldNamesMustBeginWithLowerCaseLetter", Justification = "Need to use Pascal Case for MDS column names")]
        public class FunctionsSystemLogsEventSource : EventSource
        {
            internal static readonly FunctionsSystemLogsEventSource Instance = new FunctionsSystemLogsEventSource();

            [Event(65520, Level = EventLevel.Verbose, Channel = EventChannel.Operational, Version = 1)]
            public void RaiseFunctionsEventVerbose(string SubscriptionId, string AppName, string FunctionName, string EventName, string Source, string Details, string Summary, string HostVersion, string EventTimestamp)
            {
                if (IsEnabled())
                {
                    WriteEvent(65520, SubscriptionId, AppName, FunctionName, EventName, Source, Details, Summary, HostVersion, EventTimestamp);
                }
            }

            [Event(65521, Level = EventLevel.Informational, Channel = EventChannel.Operational, Version = 1)]
            public void RaiseFunctionsEventInfo(string SubscriptionId, string AppName, string FunctionName, string EventName, string Source, string Details, string Summary, string HostVersion, string EventTimestamp)
            {
                if (IsEnabled())
                {
                    WriteEvent(65521, SubscriptionId, AppName, FunctionName, EventName, Source, Details, Summary, HostVersion, EventTimestamp);
                }
            }

            [Event(65522, Level = EventLevel.Warning, Channel = EventChannel.Operational, Version = 1)]
            public void RaiseFunctionsEventWarning(string SubscriptionId, string AppName, string FunctionName, string EventName, string Source, string Details, string Summary, string HostVersion, string EventTimestamp)
            {
                if (IsEnabled())
                {
                    WriteEvent(65522, SubscriptionId, AppName, FunctionName, EventName, Source, Details, Summary, HostVersion, EventTimestamp);
                }
            }

            [Event(65523, Level = EventLevel.Error, Channel = EventChannel.Operational, Version = 1)]
            public void RaiseFunctionsEventError(string SubscriptionId, string AppName, string FunctionName, string EventName, string Source, string Details, string Summary, string HostVersion, string EventTimestamp)
            {
                if (IsEnabled())
                {
                    WriteEvent(65523, SubscriptionId, AppName, FunctionName, EventName, Source, Details, Summary, HostVersion, EventTimestamp);
                }
            }

            [Event(65524, Level = EventLevel.Informational, Channel = EventChannel.Operational, Version = 1)]
            public void LogFunctionMetricEvent(string SubscriptionId, string AppName, string FunctionName, string EventName, long Average, long Minimum, long Maximum, long Count, string HostVersion, string EventTimestamp)
            {
                if (IsEnabled())
                {
                    WriteEvent(65524, SubscriptionId, AppName, FunctionName, EventName, Average, Minimum, Maximum, Count, HostVersion, EventTimestamp);
                }
            }
        }
    }
}
