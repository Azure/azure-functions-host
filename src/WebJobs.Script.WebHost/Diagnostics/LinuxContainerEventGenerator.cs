// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    internal class LinuxContainerEventGenerator : LinuxEventGenerator
    {
        private readonly Action<string> _writeEvent;
        private readonly bool _consoleEnabled = true;

        public LinuxContainerEventGenerator(Action<string> writeEvent = null)
        {
            _writeEvent = writeEvent ?? ConsoleWriter;

            if (Environment.GetEnvironmentVariable(EnvironmentSettingNames.ConsoleLoggingDisabled) == "1")
            {
                _consoleEnabled = false;
            }
        }

        // Note: the strange escaping of backslashes in these expressions for string literals (e.g. '\\\\\"') is because
        // of the current JSON serialization our log messages undergoe.
        public static string TraceEventRegex { get; } = $"{ScriptConstants.LinuxLogEventStreamName} (?<Level>[0-6]),(?<SubscriptionId>[^,]*),(?<AppName>[^,]*),(?<FunctionName>[^,]*),(?<EventName>[^,]*),(?<Source>[^,]*),\\\\\"(?<Details>.*)\\\\\",\\\\\"(?<Summary>.*)\\\\\",(?<HostVersion>[^,]*),(?<EventTimestamp>[^,]+),(?<ExceptionType>[^,]*),\\\\\"(?<ExceptionMessage>.*)\\\\\",(?<FunctionInvocationId>[^,]*),(?<HostInstanceId>[^,]*),(?<ActivityId>[^,\"]*)";

        public static string MetricEventRegex { get; } = $"{ScriptConstants.LinuxMetricEventStreamName} (?<SubscriptionId>[^,]*),(?<AppName>[^,]*),(?<FunctionName>[^,]*),(?<EventName>[^,]*),(?<Average>\\d*),(?<Min>\\d*),(?<Max>\\d*),(?<Count>\\d*),(?<HostVersion>[^,]*),(?<EventTimestamp>[^,\"]+)";

        public static string DetailsEventRegex { get; } = $"{ScriptConstants.LinuxFunctionDetailsEventStreamName} (?<AppName>[^,]*),(?<FunctionName>[^,]*),\\\\\"(?<InputBindings>.*)\\\\\",\\\\\"(?<OutputBindings>.*)\\\\\",(?<ScriptType>[^,]*),(?<IsDisabled>[0|1])";

        public override void LogFunctionTraceEvent(LogLevel level, string subscriptionId, string appName, string functionName, string eventName, string source, string details, string summary, string exceptionType, string exceptionMessage, string functionInvocationId, string hostInstanceId, string activityId)
        {
            string eventTimestamp = DateTime.UtcNow.ToString(EventTimestampFormat);
            string hostVersion = ScriptHost.Version;
            FunctionsSystemLogsEventSource.Instance.SetActivityId(activityId);

            _writeEvent($"{ScriptConstants.LinuxLogEventStreamName} {(int)ToEventLevel(level)},{subscriptionId},{appName},{functionName},{eventName},{source},{NormalizeString(details)},{NormalizeString(summary)},{hostVersion},{eventTimestamp},{exceptionType},{NormalizeString(exceptionMessage)},{functionInvocationId},{hostInstanceId},{activityId}");
        }

        public override void LogFunctionMetricEvent(string subscriptionId, string appName, string functionName, string eventName, long average, long minimum, long maximum, long count, DateTime eventTimestamp)
        {
            string hostVersion = ScriptHost.Version;

            _writeEvent($"{ScriptConstants.LinuxMetricEventStreamName} {subscriptionId},{appName},{functionName},{eventName},{average},{minimum},{maximum},{count},{hostVersion},{eventTimestamp.ToString(EventTimestampFormat)}");
        }

        public override void LogFunctionDetailsEvent(string siteName, string functionName, string inputBindings, string outputBindings, string scriptType, bool isDisabled)
        {
            _writeEvent($"{ScriptConstants.LinuxFunctionDetailsEventStreamName} {siteName},{functionName},{NormalizeString(inputBindings)},{NormalizeString(outputBindings)},{scriptType},{(isDisabled ? 1 : 0)}");
        }

        public override void LogFunctionExecutionAggregateEvent(string siteName, string functionName, long executionTimeInMs, long functionStartedCount, long functionCompletedCount, long functionFailedCount)
        {
        }

        public override void LogFunctionExecutionEvent(string executionId, string siteName, int concurrency, string functionName, string invocationId, string executionStage, long executionTimeSpan, bool success)
        {
        }

        private void ConsoleWriter(string evt)
        {
            if (_consoleEnabled)
            {
                Console.WriteLine(evt);
            }
        }

        public override void LogAzureMonitorDiagnosticLogEvent(LogLevel level, string resourceId, string operationName, string category, string regionName, string properties)
        {
        }
    }
}