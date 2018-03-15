// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Metrics;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    internal class LinuxContainerEventGenerator : IEventGenerator
    {
        private const string EventTimestamp = "MM/dd/yyyy hh:mm:ss.fff tt";
        private readonly IFunctionExecutionMonitor _functionMonitor;

        public LinuxContainerEventGenerator(IFunctionExecutionMonitor functionMonitor)
        {
            _functionMonitor = functionMonitor;
        }

        public void LogFunctionTraceEvent(LogLevel level, string subscriptionId, string appName, string functionName, string eventName, string source, string details, string summary, string exceptionType, string exceptionMessage, string functionInvocationId, string hostInstanceId, string activityId)
        {
            string eventTimestamp = DateTime.UtcNow.ToString(EventTimestamp);
            FunctionsSystemLogsEventSource.Instance.SetActivityId(activityId);

            // TODO: add all columns and format correctly
            Console.WriteLine($"SYSTEM {eventTimestamp} {subscriptionId} {appName} {functionName} {level} {summary} {details}");
        }

        public void LogFunctionMetricEvent(string subscriptionId, string appName, string functionName, string eventName, long average, long minimum, long maximum, long count, DateTime eventTimestamp)
        {
            // TODO: add all columns and format correctly
            Console.WriteLine($"METRICS {eventTimestamp} {subscriptionId} {appName} {functionName} {eventName} {average} {minimum} {maximum} {count}");
        }

        public void LogFunctionExecutionAggregateEvent(string siteName, string functionName, long executionTimeInMs, long functionStartedCount, long functionCompletedCount, long functionFailedCount)
        {
            // TODO - doesn't map to a table
            // used for Antares internal communication
        }

        public void LogFunctionDetailsEvent(string siteName, string functionName, string inputBindings, string outputBindings, string scriptType, bool isDisabled)
        {
            // TODO - these events are logged to AntaresFunctionSdkEvents
        }

        public void LogFunctionExecutionEvent(string executionId, string siteName, int concurrency, string functionName, string invocationId, string executionStage, long executionTimeSpan, bool success)
        {
            // doesn't map to a table. Used for Antares internal communication

            var status = new FunctionExecutionStatus
            {
                ExecutionId = executionId,
                SiteName = siteName,
                Concurrency = concurrency,
                FunctionName = functionName,
                InvocationId = invocationId,
                CurrentExecutionStage = (FunctionExecutionStage)Enum.Parse(typeof(FunctionExecutionStage), executionStage),
                ExecutionTimeSpanInMs = executionTimeSpan,
                IsSucceeded = success
            };
            _functionMonitor.FunctionExecution(status);
        }
    }
}