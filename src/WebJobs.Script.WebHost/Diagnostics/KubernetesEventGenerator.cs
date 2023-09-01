// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    internal class KubernetesEventGenerator : LinuxEventGenerator
    {
        private const int MaxDetailsLength = 10000;
        private readonly Action<string> _writeEvent;

        public KubernetesEventGenerator(Action<string> writeEvent = null)
        {
            _writeEvent = writeEvent ?? ConsoleWriter;
        }

        public override void LogFunctionTraceEvent(LogLevel level, string subscriptionId, string appName, string functionName, string eventName, string source, string details, string summary, string exceptionType, string exceptionMessage, string functionInvocationId, string hostInstanceId, string activityId, string runtimeSiteName, string slotName, DateTime eventTimestamp)
        {
            using (FunctionsSystemLogsEventSource.SetActivityId(activityId))
            {
                details = details.Length > MaxDetailsLength ? details.Substring(0, MaxDetailsLength) : details;

                // Set event type to MS_FUNCTION_LOGS to send these events as part of infra logs.
                JObject traceLog = new JObject();
                traceLog.Add("EventType", ScriptConstants.LinuxLogEventStreamName);
                traceLog.Add("Level", (int)ToEventLevel(level));
                traceLog.Add("SubscriptionId", subscriptionId);
                traceLog.Add("AppName", appName);
                traceLog.Add("FunctionName", functionName);
                traceLog.Add("EventName", eventName);
                traceLog.Add("Source", source);
                traceLog.Add("Details", NormalizeString(details, addEnclosingQuotes: false));
                traceLog.Add("Summary", NormalizeString(summary, addEnclosingQuotes: false));
                traceLog.Add("HostVersion", ScriptHost.Version);
                traceLog.Add("EventTimeStamp", eventTimestamp.ToString(EventTimestampFormat));
                traceLog.Add("ExceptionType", exceptionType);
                traceLog.Add("ExceptionMessage", NormalizeString(exceptionMessage, addEnclosingQuotes: false));
                traceLog.Add("FunctionInvocationId", functionInvocationId);
                traceLog.Add("HostInstanceId", hostInstanceId);
                traceLog.Add("ActivityId", activityId);
                traceLog.Add("RuntimeSiteName", runtimeSiteName);
                traceLog.Add("SlotName", slotName);

                _writeEvent(traceLog.ToString(Formatting.None));
            }
        }

        public override void LogFunctionMetricEvent(string subscriptionId, string appName, string functionName, string eventName, long average, long minimum, long maximum, long count, DateTime eventTimestamp, string data, string runtimeSiteName, string slotName)
        {
            // Set event type to MS_FUNCTION_METRICS to send these events as part of infra logs.
            JObject metricEvent = new JObject();
            metricEvent.Add("EventType", ScriptConstants.LinuxMetricEventStreamName);
            metricEvent.Add("SubscriptionId", subscriptionId);
            metricEvent.Add("AppName", appName);
            metricEvent.Add("FunctionName", functionName);
            metricEvent.Add("EventName", eventName);
            metricEvent.Add("Average", average);
            metricEvent.Add("Minimum", minimum);
            metricEvent.Add("Maximum", maximum);
            metricEvent.Add("Count", count);
            metricEvent.Add("HostVersion", ScriptHost.Version);
            metricEvent.Add("EventTimeStamp", eventTimestamp.ToString(EventTimestampFormat));
            metricEvent.Add("Data", NormalizeString(data));
            metricEvent.Add("RuntimeSiteName", runtimeSiteName);
            metricEvent.Add("SlotName", slotName);

            _writeEvent(metricEvent.ToString(Formatting.None));
        }

        public override void LogFunctionDetailsEvent(string siteName, string functionName, string inputBindings, string outputBindings, string scriptType, bool isDisabled)
        {
        }

        public override void LogFunctionExecutionAggregateEvent(string siteName, string functionName, long executionTimeInMs, long functionStartedCount, long functionCompletedCount, long functionFailedCount)
        {
        }

        // The execution event state is not stored anywhere in Kubernetes environments since all
        // scaling is driven by KEDA. So we won't log this event.

        public override void LogFunctionExecutionEvent(string executionId, string siteName, int concurrency, string functionName, string invocationId, string executionStage, long executionTimeSpan, bool success)
        {
        }

        public override void LogAzureMonitorDiagnosticLogEvent(LogLevel level, string resourceId, string operationName, string category, string regionName, string properties)
        {
            JObject azMonEvent = new JObject();
            // Set event type to MS_FUNCTION_AZURE_MONITOR_EVENT to send these event to customer's
            // azure monitor workspace.
            azMonEvent.Add("EventType", ScriptConstants.LinuxAzureMonitorEventStreamName);
            azMonEvent.Add("Level", (int)ToEventLevel(level));
            azMonEvent.Add("ResourceId", resourceId);
            azMonEvent.Add("OperationName", operationName);
            azMonEvent.Add("Category", category);
            azMonEvent.Add("RegionName", regionName);
            azMonEvent.Add("Properties", NormalizeString(properties.Replace("'", string.Empty)));

            _writeEvent(azMonEvent.ToString(Formatting.None));
        }

        private void ConsoleWriter(string evt)
        {
            Console.WriteLine(evt);
        }
    }
}