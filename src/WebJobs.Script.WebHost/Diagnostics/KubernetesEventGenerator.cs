// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    internal class KubernetesEventGenerator : LinuxEventGenerator
    {
        private const int MaxDetailsLength = 10000;
        private readonly string _podName;
        private readonly Action<string> _writeEvent;

        public KubernetesEventGenerator(IEnvironment environment, Action<string> writeEvent = null)
        {
            _podName = environment.GetEnvironmentVariable(EnvironmentSettingNames.PodName);
            _writeEvent = writeEvent ?? ConsoleWriter;
        }

        public override void LogFunctionTraceEvent(LogLevel level, string subscriptionId, string appName, string functionName, string eventName, string source, string details, string summary, string exceptionType, string exceptionMessage, string functionInvocationId, string hostInstanceId, string activityId, string runtimeSiteName, string slotName, DateTime eventTimestamp)
        {
            string formattedEventTimeStamp = eventTimestamp.ToString(EventTimestampFormat);
            string hostVersion = ScriptHost.Version;
            FunctionsSystemLogsEventSource.Instance.SetActivityId(activityId);
            details = details.Length > MaxDetailsLength ? details.Substring(0, MaxDetailsLength) : details;

            _writeEvent($"{(int)ToEventLevel(level)},{subscriptionId},{appName},{functionName},{eventName},{source},{NormalizeString(details)},{NormalizeString(summary)},{hostVersion},{formattedEventTimeStamp},{exceptionType},{NormalizeString(exceptionMessage)},{functionInvocationId},{hostInstanceId},{activityId},{runtimeSiteName},{slotName},{_podName}");
        }

        public override void LogFunctionMetricEvent(string subscriptionId, string appName, string functionName, string eventName, long average, long minimum, long maximum, long count, DateTime eventTimestamp, string data, string runtimeSiteName, string slotName)
        {
            string hostVersion = ScriptHost.Version;

            _writeEvent($"{subscriptionId},{appName},{functionName},{eventName},{average},{minimum},{maximum},{count},{hostVersion},{eventTimestamp.ToString(EventTimestampFormat)},{NormalizeString(data)},{runtimeSiteName},{slotName},{_podName}");
        }

        public override void LogFunctionDetailsEvent(string siteName, string functionName, string inputBindings, string outputBindings, string scriptType, bool isDisabled)
        {
            _writeEvent($"{siteName},{functionName},{NormalizeString(inputBindings)},{NormalizeString(outputBindings)},{scriptType},{(isDisabled ? 1 : 0)},{_podName}");
        }

        public override void LogFunctionExecutionAggregateEvent(string siteName, string functionName, long executionTimeInMs, long functionStartedCount, long functionCompletedCount, long functionFailedCount)
        {
            _writeEvent($"{siteName},{functionName},{executionTimeInMs},{functionStartedCount},{functionCompletedCount},{functionFailedCount},{_podName}");
        }

        public override void LogFunctionExecutionEvent(string executionId, string siteName, int concurrency, string functionName, string invocationId, string executionStage, long executionTimeSpan, bool success)
        {
            _writeEvent($"{executionId},{siteName},{concurrency},{functionName},{invocationId},{executionStage},{executionTimeSpan},{success},{_podName}");
        }

        public override void LogAzureMonitorDiagnosticLogEvent(LogLevel level, string resourceId, string operationName, string category, string regionName, string properties)
        {
            _writeEvent($"{(int)ToEventLevel(level)},{resourceId},{operationName},{category},{regionName},{NormalizeString(properties.Replace("'", string.Empty))},{DateTime.UtcNow.ToString()},{_podName}");
        }

        private void ConsoleWriter(string evt)
        {
            Console.WriteLine(evt);
        }
    }
}