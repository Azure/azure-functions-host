// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics.Tracing;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public abstract class LinuxEventGenerator : IEventGenerator
    {
        public static readonly string EventTimestampFormat = "O";

        // These names should match the source file names for fluentd
        public static readonly string FunctionsLogsCategory = "functionslogsv2";
        public static readonly string FunctionsMetricsCategory = "functionsmetrics";
        public static readonly string FunctionsDetailsCategory = "functionsdetails";
        public static readonly string FunctionsExecutionEventsCategory = "functionexecutionevents";

        internal static string NormalizeString(string value, bool addEnclosingQuotes = true)
        {
            // Need to remove newlines for csv output
            value = value.Replace(Environment.NewLine, " ");

            // Need to replace double quotes with single quotes as
            // our regex query looks at double quotes as delimeter for
            // individual column
            // TODO: Once the regex takes into account for quotes, we can
            // safely remove this
            value = value.Replace("\"", "'");

            // Wrap string literals in enclosing quotes
            // For string columns that may contain quotes and/or
            // our delimiter ',', before writing the value we
            // enclose in quotes. This allows us to define matching
            // groups based on quotes for these values.
            return addEnclosingQuotes ? $"\"{value}\"" : value;
        }

        /// <summary>
        /// Performs the same mapping from <see cref="LogLevel"/> to <see cref="EventLevel"/>
        /// that is performed for ETW event logging in <see cref="EtwEventGenerator"/>, so we
        /// have consistent log levels across platforms.
        /// </summary>
        internal static EventLevel ToEventLevel(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Trace:
                case LogLevel.Debug:
                    return EventLevel.Verbose;
                case LogLevel.Information:
                    return EventLevel.Informational;
                case LogLevel.Warning:
                    return EventLevel.Warning;
                case LogLevel.Error:
                    return EventLevel.Error;
                case LogLevel.Critical:
                    return EventLevel.Critical;
                default:
                    return EventLevel.LogAlways;
            }
        }

        public abstract void LogFunctionTraceEvent(LogLevel level, string subscriptionId, string appName,
            string functionName, string eventName,
            string source, string details, string summary, string exceptionType, string exceptionMessage,
            string functionInvocationId, string hostInstanceId, string activityId, string runtimeSiteName, string slotName, DateTime eventTimestamp);

        public abstract void LogFunctionMetricEvent(string subscriptionId, string appName, string functionName,
            string eventName, long average,
            long minimum, long maximum, long count, DateTime eventTimestamp, string data, string runtimeSiteName, string slotName);

        public abstract void LogFunctionExecutionAggregateEvent(string siteName, string functionName,
            long executionTimeInMs,
            long functionStartedCount, long functionCompletedCount, long functionFailedCount);

        public abstract void LogFunctionDetailsEvent(string siteName, string functionName, string inputBindings,
            string outputBindings,
            string scriptType, bool isDisabled);

        public abstract void LogFunctionExecutionEvent(string executionId, string siteName, int concurrency,
            string functionName,
            string invocationId, string executionStage, long executionTimeSpan, bool success);

        public abstract void LogAzureMonitorDiagnosticLogEvent(LogLevel level, string resourceId, string operationName,
            string category, string regionName, string properties);
    }
}
