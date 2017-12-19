// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Tracing;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    internal class EventGenerator : IEventGenerator
    {
        private const string EventTimestamp = "MM/dd/yyyy hh:mm:ss.fff tt";

        public void LogFunctionTraceEvent(TraceLevel level, string subscriptionId, string appName, string functionName, string eventName, string source, string details, string summary, string exceptionType, string exceptionMessage)
        {
            string eventTimestamp = DateTime.UtcNow.ToString(EventTimestamp);
            switch (level)
            {
                case TraceLevel.Verbose:
                    FunctionsSystemLogsEventSource.Instance.RaiseFunctionsEventVerbose(subscriptionId, appName, functionName, eventName, source, details, summary, ScriptHost.Version, eventTimestamp);
                    break;
                case TraceLevel.Info:
                    FunctionsSystemLogsEventSource.Instance.RaiseFunctionsEventInfo(subscriptionId, appName, functionName, eventName, source, details, summary, ScriptHost.Version, eventTimestamp);
                    break;
                case TraceLevel.Warning:
                    FunctionsSystemLogsEventSource.Instance.RaiseFunctionsEventWarning(subscriptionId, appName, functionName, eventName, source, details, summary, ScriptHost.Version, eventTimestamp);
                    break;
                case TraceLevel.Error:
                    FunctionsSystemLogsEventSource.Instance.RaiseFunctionsEventError(subscriptionId, appName, functionName, eventName, source, details, summary, ScriptHost.Version, eventTimestamp, exceptionType, exceptionMessage);
                    break;
            }
        }

        public void LogFunctionMetricEvent(string subscriptionId, string appName, string functionName, string eventName, long average, long minimum, long maximum, long count, DateTime eventTimestamp)
        {
            FunctionsSystemLogsEventSource.Instance.LogFunctionMetricEvent(subscriptionId, appName, functionName, eventName, average, minimum, maximum, count, ScriptHost.Version, eventTimestamp.ToString(EventTimestamp));
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

        public unsafe class ExtendedEventSource : EventSource
        {
            [NonEvent]
            [Obsolete("Do not use params object[] overload, create a explicit WriteEvent in ExtendedEventSource.", true)]
            protected new void WriteEvent(int eventNum, params object[] args)
            {
                base.WriteEvent(eventNum, args);
            }

            // LogFunctionExecutionAggregateEvent
            [NonEvent]
            protected void WriteEvent(int eventNum, string a, string b, ulong c, ulong d, ulong e, ulong f)
            {
                const int count = 6;
                fixed (char* aPtr = a, bPtr = b)
                {
                    EventData* data = stackalloc EventData[count];
                    data[0].DataPointer = (IntPtr)aPtr;
                    data[0].Size = (a.Length + 1) * sizeof(char);
                    data[1].DataPointer = (IntPtr)bPtr;
                    data[1].Size = (b.Length + 1) * sizeof(char);
                    data[2].DataPointer = (IntPtr)(&c);
                    data[2].Size = sizeof(ulong);
                    data[3].DataPointer = (IntPtr)(&d);
                    data[3].Size = sizeof(ulong);
                    data[4].DataPointer = (IntPtr)(&e);
                    data[4].Size = sizeof(ulong);
                    data[5].DataPointer = (IntPtr)(&f);
                    data[5].Size = sizeof(ulong);

                    WriteEventCore(eventNum, count, data);
                }
            }

            // LogFunctionDetailsEvent
            [NonEvent]
            protected void WriteEvent(int eventNum, string a, string b, string c, string d, string e, bool f)
            {
                const int count = 6;
                fixed (char* aPtr = a,
                    bPtr = b,
                    cPtr = c,
                    dPtr = d,
                    ePtr = e)
                {
                    EventData* data = stackalloc EventData[count];
                    data[0].DataPointer = (IntPtr)aPtr;
                    data[0].Size = (a.Length + 1) * sizeof(char);
                    data[1].DataPointer = (IntPtr)bPtr;
                    data[1].Size = (b.Length + 1) * sizeof(char);
                    data[2].DataPointer = (IntPtr)cPtr;
                    data[2].Size = (c.Length + 1) * sizeof(char);
                    data[3].DataPointer = (IntPtr)dPtr;
                    data[3].Size = (d.Length + 1) * sizeof(char);
                    data[4].DataPointer = (IntPtr)ePtr;
                    data[4].Size = (e.Length + 1) * sizeof(char);
                    data[5].DataPointer = (IntPtr)(&f);
                    data[5].Size = 4; // boolean variables have size 4

                    WriteEventCore(eventNum, count, data);
                }
            }

            // LogFunctionExecutionEvent
            [NonEvent]
            protected void WriteEvent(int eventNum, string a, string b, int c, string d, string e, string f, ulong g, bool h)
            {
                const int count = 8;
                fixed (char* aPtr = a,
                    bPtr = b,
                    dPtr = d,
                    ePtr = e,
                    fPtr = f)
                {
                    EventData* data = stackalloc EventData[count];
                    data[0].DataPointer = (IntPtr)aPtr;
                    data[0].Size = (a.Length + 1) * sizeof(char);
                    data[1].DataPointer = (IntPtr)bPtr;
                    data[1].Size = (b.Length + 1) * sizeof(char);
                    data[2].DataPointer = (IntPtr)(&c);
                    data[2].Size = sizeof(int);
                    data[3].DataPointer = (IntPtr)dPtr;
                    data[3].Size = (d.Length + 1) * sizeof(char);
                    data[4].DataPointer = (IntPtr)ePtr;
                    data[4].Size = (e.Length + 1) * sizeof(char);
                    data[5].DataPointer = (IntPtr)fPtr;
                    data[5].Size = (f.Length + 1) * sizeof(char);
                    data[6].DataPointer = (IntPtr)(&g);
                    data[6].Size = sizeof(ulong);
                    data[7].DataPointer = (IntPtr)(&h);
                    data[7].Size = 4; // boolean variables have size 4

                    WriteEventCore(eventNum, count, data);
                }
            }

            // RaiseFunctionsEventVerbose/Info/Warning
            [NonEvent]
            protected void WriteEvent(int eventNum, string a, string b, string c, string d, string e, string f, string g, string h, string i)
            {
                const int count = 9;
                fixed (char* aPtr = a,
                    bPtr = b,
                    cPtr = c,
                    dPtr = d,
                    ePtr = e,
                    fPtr = f,
                    gPtr = g,
                    hPtr = h,
                    iPtr = i)
                {
                    EventData* data = stackalloc EventData[count];
                    data[0].DataPointer = (IntPtr)aPtr;
                    data[0].Size = (a.Length + 1) * sizeof(char);
                    data[1].DataPointer = (IntPtr)bPtr;
                    data[1].Size = (b.Length + 1) * sizeof(char);
                    data[2].DataPointer = (IntPtr)cPtr;
                    data[2].Size = (c.Length + 1) * sizeof(char);
                    data[3].DataPointer = (IntPtr)dPtr;
                    data[3].Size = (d.Length + 1) * sizeof(char);
                    data[4].DataPointer = (IntPtr)ePtr;
                    data[4].Size = (e.Length + 1) * sizeof(char);
                    data[5].DataPointer = (IntPtr)fPtr;
                    data[5].Size = (f.Length + 1) * sizeof(char);
                    data[6].DataPointer = (IntPtr)gPtr;
                    data[6].Size = (g.Length + 1) * sizeof(char);
                    data[7].DataPointer = (IntPtr)hPtr;
                    data[7].Size = (h.Length + 1) * sizeof(char);
                    data[8].DataPointer = (IntPtr)iPtr;
                    data[8].Size = (i.Length + 1) * sizeof(char);

                    WriteEventCore(eventNum, count, data);
                }
            }

            // RaiseFunctionsEventError
            [NonEvent]
            protected void WriteEvent(int eventNum, string a, string b, string c, string d, string e, string f, string g, string h, string i, string j, string k)
            {
                const int count = 11;
                fixed (char* aPtr = a,
                    bPtr = b,
                    cPtr = c,
                    dPtr = d,
                    ePtr = e,
                    fPtr = f,
                    gPtr = g,
                    hPtr = h,
                    iPtr = i,
                    jPtr = j,
                    kPtr = k)
                {
                    EventData* data = stackalloc EventData[count];
                    data[0].DataPointer = (IntPtr)aPtr;
                    data[0].Size = (a.Length + 1) * sizeof(char);
                    data[1].DataPointer = (IntPtr)bPtr;
                    data[1].Size = (b.Length + 1) * sizeof(char);
                    data[2].DataPointer = (IntPtr)cPtr;
                    data[2].Size = (c.Length + 1) * sizeof(char);
                    data[3].DataPointer = (IntPtr)dPtr;
                    data[3].Size = (d.Length + 1) * sizeof(char);
                    data[4].DataPointer = (IntPtr)ePtr;
                    data[4].Size = (e.Length + 1) * sizeof(char);
                    data[5].DataPointer = (IntPtr)fPtr;
                    data[5].Size = (f.Length + 1) * sizeof(char);
                    data[6].DataPointer = (IntPtr)gPtr;
                    data[6].Size = (g.Length + 1) * sizeof(char);
                    data[7].DataPointer = (IntPtr)hPtr;
                    data[7].Size = (h.Length + 1) * sizeof(char);
                    data[8].DataPointer = (IntPtr)iPtr;
                    data[8].Size = (i.Length + 1) * sizeof(char);
                    data[9].DataPointer = (IntPtr)jPtr;
                    data[9].Size = (j.Length + 1) * sizeof(char);
                    data[10].DataPointer = (IntPtr)kPtr;
                    data[10].Size = (k.Length + 1) * sizeof(char);
                    WriteEventCore(eventNum, count, data);
                }
            }

            // LogFunctionMetricEvent
            [NonEvent]
            protected void WriteEvent(int eventNum, string a, string b, string c, string d, long e, long f, long g, long h, string i, string j)
            {
                const int count = 10;
                fixed (char* aPtr = a,
                    bPtr = b,
                    cPtr = c,
                    dPtr = d,
                    iPtr = i,
                    jPtr = j)
                {
                    EventData* data = stackalloc EventData[count];
                    data[0].DataPointer = (IntPtr)aPtr;
                    data[0].Size = (a.Length + 1) * sizeof(char);
                    data[1].DataPointer = (IntPtr)bPtr;
                    data[1].Size = (b.Length + 1) * sizeof(char);
                    data[2].DataPointer = (IntPtr)cPtr;
                    data[2].Size = (c.Length + 1) * sizeof(char);
                    data[3].DataPointer = (IntPtr)dPtr;
                    data[3].Size = (d.Length + 1) * sizeof(char);
                    data[4].DataPointer = (IntPtr)(&e);
                    data[4].Size = sizeof(long);
                    data[5].DataPointer = (IntPtr)(&f);
                    data[5].Size = sizeof(long);
                    data[6].DataPointer = (IntPtr)(&g);
                    data[6].Size = sizeof(long);
                    data[7].DataPointer = (IntPtr)(&h);
                    data[7].Size = sizeof(long);
                    data[8].DataPointer = (IntPtr)iPtr;
                    data[8].Size = (i.Length + 1) * sizeof(char);
                    data[9].DataPointer = (IntPtr)jPtr;
                    data[9].Size = (j.Length + 1) * sizeof(char);

                    WriteEventCore(eventNum, count, data);
                }
            }
        }

        [EventSource(Guid = "08D0D743-5C24-43F9-9723-98277CEA5F9B")]
        public class FunctionsEventSource : ExtendedEventSource
        {
            internal static readonly FunctionsEventSource Instance = new FunctionsEventSource();

            [Event(57907, Level = EventLevel.Informational, Channel = EventChannel.Operational, Keywords = FunctionsKeywords.DefaultMetricsKeyword)]
            public void LogFunctionExecutionAggregateEvent(string siteName, string functionName, ulong executionTimeInMs, ulong functionStartedCount, ulong functionCompletedCount, ulong functionFailedCount)
            {
                if (IsEnabled())
                {
                    WriteEvent(57907, siteName, functionName, executionTimeInMs, functionStartedCount, functionCompletedCount, functionFailedCount);
                }
            }

            [Event(57908, Level = EventLevel.Informational, Channel = EventChannel.Operational, Keywords = FunctionsKeywords.DefaultMetricsKeyword)]
            public void LogFunctionDetailsEvent(string siteName, string functionName, string inputBindings, string outputBindings, string scriptType, bool isDisabled)
            {
                if (IsEnabled())
                {
                    WriteEvent(57908, siteName, functionName, inputBindings, outputBindings, scriptType, isDisabled);
                }
            }

            [Event(57909, Level = EventLevel.Informational, Channel = EventChannel.Operational, Keywords = FunctionsKeywords.DefaultMetricsKeyword)]
            public void LogFunctionExecutionEvent(string executionId, string siteName, int concurrency, string functionName, string invocationId, string executionStage, ulong executionTimeSpan, bool success)
            {
                if (IsEnabled())
                {
                    WriteEvent(57909, executionId, siteName, concurrency, functionName, invocationId, executionStage, executionTimeSpan, success);
                }
            }

            public class FunctionsKeywords
            {
                public const EventKeywords DefaultMetricsKeyword = unchecked((EventKeywords)0x8000000000000000);
            }
        }

        [EventSource(Guid = "a7044dd6-c8ef-4980-858c-942d972b6250")]
        [SuppressMessage("Microsoft.StyleCop.CSharp.NamingRules", "SA1313:FieldNamesMustBeginWithLowerCaseLetter", Justification = "Need to use Pascal Case for MDS column names")]
        public class FunctionsSystemLogsEventSource : ExtendedEventSource
        {
            internal static readonly FunctionsSystemLogsEventSource Instance = new FunctionsSystemLogsEventSource();

            [Event(65520, Level = EventLevel.Verbose, Channel = EventChannel.Operational, Version = 2)]
            public void RaiseFunctionsEventVerbose(string SubscriptionId, string AppName, string FunctionName, string EventName, string Source, string Details, string Summary, string HostVersion, string EventTimestamp)
            {
                if (IsEnabled())
                {
                    WriteEvent(65520, SubscriptionId, AppName, FunctionName, EventName, Source, Details, Summary, HostVersion, EventTimestamp);
                }
            }

            [Event(65521, Level = EventLevel.Informational, Channel = EventChannel.Operational, Version = 2)]
            public void RaiseFunctionsEventInfo(string SubscriptionId, string AppName, string FunctionName, string EventName, string Source, string Details, string Summary, string HostVersion, string EventTimestamp)
            {
                if (IsEnabled())
                {
                    WriteEvent(65521, SubscriptionId, AppName, FunctionName, EventName, Source, Details, Summary, HostVersion, EventTimestamp);
                }
            }

            [Event(65522, Level = EventLevel.Warning, Channel = EventChannel.Operational, Version = 2)]
            public void RaiseFunctionsEventWarning(string SubscriptionId, string AppName, string FunctionName, string EventName, string Source, string Details, string Summary, string HostVersion, string EventTimestamp)
            {
                if (IsEnabled())
                {
                    WriteEvent(65522, SubscriptionId, AppName, FunctionName, EventName, Source, Details, Summary, HostVersion, EventTimestamp);
                }
            }

            [Event(65523, Level = EventLevel.Error, Channel = EventChannel.Operational, Version = 2)]
            public void RaiseFunctionsEventError(string SubscriptionId, string AppName, string FunctionName, string EventName, string Source, string Details, string Summary, string HostVersion, string EventTimestamp, string InnerExceptionType, string InnerExceptionMessage)
            {
                if (IsEnabled())
                {
                    WriteEvent(65523, SubscriptionId, AppName, FunctionName, EventName, Source, Details, Summary, HostVersion, EventTimestamp, InnerExceptionType, InnerExceptionMessage);
                }
            }

            [Event(65524, Level = EventLevel.Informational, Channel = EventChannel.Operational, Version = 2)]
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