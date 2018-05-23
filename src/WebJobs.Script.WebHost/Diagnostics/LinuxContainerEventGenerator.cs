﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics.Tracing;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    internal class LinuxContainerEventGenerator : IEventGenerator
    {
        private const string EventTimestampFormat = "MM/dd/yyyy hh:mm:ss.fff tt";
        private readonly Action<string> _writeEvent;

        public LinuxContainerEventGenerator(Action<string> writeEvent = null)
        {
            _writeEvent = writeEvent ?? ConsoleWriter;
        }

        // Note: the strange escaping of backslashes in these expressions for string literals (e.g. '\\\\\"') is because
        // of the current JSON serialization our log messages undergoe.
        public static string TraceEventRegex { get; } = $"{ScriptConstants.LinuxLogEventStreamName} (?<Level>[0-6]),(?<SubscriptionId>[^,]*),(?<AppName>[^,]*),(?<FunctionName>[^,]*),(?<EventName>[^,]*),(?<Source>[^,]*),\\\\\"(?<Details>.*)\\\\\",\\\\\"(?<Summary>.*)\\\\\",(?<HostVersion>[^,]*),(?<EventTimestamp>[^,]+),(?<ExceptionType>[^,]*),\\\\\"(?<ExceptionMessage>.*)\\\\\",(?<FunctionInvocationId>[^,]*),(?<HostInstanceId>[^,]*),(?<ActivityId>[^,\"]*)";

        public static string MetricEventRegex { get; } = $"{ScriptConstants.LinuxMetricEventStreamName} (?<SubscriptionId>[^,]*),(?<AppName>[^,]*),(?<FunctionName>[^,]*),(?<EventName>[^,]*),(?<Average>\\d*),(?<Min>\\d*),(?<Max>\\d*),(?<Count>\\d*),(?<HostVersion>[^,]*),(?<EventTimestamp>[^,\"]+)";

        public static string DetailsEventRegex { get; } = $"{ScriptConstants.LinuxFunctionDetailsEventStreamName} (?<AppName>[^,]*),(?<FunctionName>[^,]*),\\\\\"(?<InputBindings>.*)\\\\\",\\\\\"(?<OutputBindings>.*)\\\\\",(?<ScriptType>[^,]*),(?<IsDisabled>[0|1])";

        public void LogFunctionTraceEvent(LogLevel level, string subscriptionId, string appName, string functionName, string eventName, string source, string details, string summary, string exceptionType, string exceptionMessage, string functionInvocationId, string hostInstanceId, string activityId)
        {
            string eventTimestamp = DateTime.UtcNow.ToString(EventTimestampFormat);
            string hostVersion = ScriptHost.Version;
            FunctionsSystemLogsEventSource.Instance.SetActivityId(activityId);

            _writeEvent($"{ScriptConstants.LinuxLogEventStreamName} {(int)ToEventLevel(level)},{subscriptionId},{appName},{functionName},{eventName},{source},{NormalizeString(details)},{NormalizeString(summary)},{hostVersion},{eventTimestamp},{exceptionType},{NormalizeString(exceptionMessage)},{functionInvocationId},{hostInstanceId},{activityId}");
        }

        public void LogFunctionMetricEvent(string subscriptionId, string appName, string functionName, string eventName, long average, long minimum, long maximum, long count, DateTime eventTimestamp)
        {
            string hostVersion = ScriptHost.Version;

            _writeEvent($"{ScriptConstants.LinuxMetricEventStreamName} {subscriptionId},{appName},{functionName},{eventName},{average},{minimum},{maximum},{count},{hostVersion},{eventTimestamp.ToString(EventTimestampFormat)}");
        }

        public void LogFunctionDetailsEvent(string siteName, string functionName, string inputBindings, string outputBindings, string scriptType, bool isDisabled)
        {
            _writeEvent($"{ScriptConstants.LinuxFunctionDetailsEventStreamName} {siteName},{functionName},{NormalizeString(inputBindings)},{NormalizeString(outputBindings)},{scriptType},{(isDisabled ? 1 : 0)}");
        }

        public void LogFunctionExecutionAggregateEvent(string siteName, string functionName, long executionTimeInMs, long functionStartedCount, long functionCompletedCount, long functionFailedCount)
        {
        }

        public void LogFunctionExecutionEvent(string executionId, string siteName, int concurrency, string functionName, string invocationId, string executionStage, long executionTimeSpan, bool success)
        {
        }

        private static void ConsoleWriter(string evt)
        {
            if (Environment.GetEnvironmentVariable(EnvironmentSettingNames.ConsoleLoggingDisabled) == "1")
            {
                return;
            }
            Console.WriteLine(evt);
        }

        internal static string NormalizeString(string value)
        {
            // need to remove newlines for csv output
            value = value.Replace(Environment.NewLine, " ");

            // Wrap string literals in enclosing quotes
            // For string columns that may contain quotes and/or
            // our delimiter ',', before writing the value we
            // enclose in quotes. This allows us to define matching
            // groups based on quotes for these values.
            return $"\"{value}\"";
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
    }
}