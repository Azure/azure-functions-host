// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    internal class EtwEventGenerator : IEventGenerator
    {
        private const string EventTimestamp = "MM/dd/yyyy hh:mm:ss.fff tt";

        public void LogFunctionTraceEvent(LogLevel level, string subscriptionId, string appName, string functionName, string eventName, string source, string details, string summary, string exceptionType, string exceptionMessage, string functionInvocationId, string hostInstanceId, string activityId)
        {
            string eventTimestamp = DateTime.UtcNow.ToString(EventTimestamp);
            FunctionsSystemLogsEventSource.Instance.SetActivityId(activityId);
            switch (level)
            {
                case LogLevel.Trace:
                case LogLevel.Debug:
                    FunctionsSystemLogsEventSource.Instance.RaiseFunctionsEventVerbose(subscriptionId, appName, functionName, eventName, source, details, summary, ScriptHost.Version, eventTimestamp, functionInvocationId, hostInstanceId);
                    break;
                case LogLevel.Information:
                    FunctionsSystemLogsEventSource.Instance.RaiseFunctionsEventInfo(subscriptionId, appName, functionName, eventName, source, details, summary, ScriptHost.Version, eventTimestamp, functionInvocationId, hostInstanceId);
                    break;
                case LogLevel.Warning:
                    FunctionsSystemLogsEventSource.Instance.RaiseFunctionsEventWarning(subscriptionId, appName, functionName, eventName, source, details, summary, ScriptHost.Version, eventTimestamp, functionInvocationId, hostInstanceId);
                    break;
                case LogLevel.Error:
                    FunctionsSystemLogsEventSource.Instance.RaiseFunctionsEventError(subscriptionId, appName, functionName, eventName, source, details, summary, ScriptHost.Version, eventTimestamp, exceptionType, exceptionMessage, functionInvocationId, hostInstanceId);
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

        public void LogFunctionDiagnosticEvent(LogLevel level, string resourceId, string operationName, string category, string regionName, string properties)
        {
            // Azure Monitor has no "Verbose" setting, so we map that to "Informational". We're controlling this logic here to minimize
            // the amount of logic we have deployed with our monitoring configuration.
            switch (level)
            {
                case LogLevel.Trace:
                case LogLevel.Debug:
                    FunctionsDiagnosticLogsEventSource.Instance.RaiseFunctionsDiagnosticEventVerbose(resourceId, operationName, category, regionName, "Informational", properties);
                    break;
                case LogLevel.Information:
                    FunctionsDiagnosticLogsEventSource.Instance.RaiseFunctionsDiagnosticEventInformational(resourceId, operationName, category, regionName, "Informational", properties);
                    break;
                case LogLevel.Warning:
                    FunctionsDiagnosticLogsEventSource.Instance.RaiseFunctionsDiagnosticEventWarning(resourceId, operationName, category, regionName, "Warning", properties);
                    break;
                case LogLevel.Error:
                case LogLevel.Critical:
                    FunctionsDiagnosticLogsEventSource.Instance.RaiseFunctionsDiagnosticEventError(resourceId, operationName, category, regionName, "Error", properties);
                    break;
            }
        }
    }
}