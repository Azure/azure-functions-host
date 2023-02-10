// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    internal class LinuxAppServiceEventGenerator : LinuxEventGenerator
    {
        private readonly Action<string> _writeEvent;
        private readonly HostNameProvider _hostNameProvider;
        private readonly IOptions<FunctionsHostingConfigOptions> _functionsHostingConfigOptions;
        private ILinuxAppServiceFileLogger _functionsExecutionEventsCategoryLogger;
        private ILinuxAppServiceFileLogger _functionsLogsCategoryLogger;
        private ILinuxAppServiceFileLogger _functionsMetricsCategoryLogger;
        private ILinuxAppServiceFileLogger _functionsDetailsCategoryLogger;

        public LinuxAppServiceEventGenerator(
            ILinuxAppServiceFileLoggerFactory loggerFactory,
            HostNameProvider hostNameProvider,
            IOptions<FunctionsHostingConfigOptions> functionsHostingConfigOptions,
            Action<string> writeEvent = null)
        {
            _writeEvent = writeEvent ?? WriteEvent;
            _hostNameProvider = hostNameProvider ?? throw new ArgumentNullException(nameof(hostNameProvider));
            _functionsHostingConfigOptions = functionsHostingConfigOptions;
            _functionsExecutionEventsCategoryLogger = loggerFactory.Create(FunctionsExecutionEventsCategory, backoffEnabled: !_functionsHostingConfigOptions.Value.DisableLinuxAppServiceLogBackoff);
            _functionsLogsCategoryLogger = loggerFactory.Create(FunctionsLogsCategory, backoffEnabled: false);
            _functionsMetricsCategoryLogger = loggerFactory.Create(FunctionsMetricsCategory, false);
            _functionsDetailsCategoryLogger = loggerFactory.Create(FunctionsDetailsCategory, false);
        }

        internal ILinuxAppServiceFileLogger FunctionsExecutionEventsCategoryLogger => _functionsExecutionEventsCategoryLogger;

        internal ILinuxAppServiceFileLogger FunctionsLogsCategoryLogger => _functionsLogsCategoryLogger;

        internal ILinuxAppServiceFileLogger FunctionsMetricsCategoryLogger => _functionsMetricsCategoryLogger;

        internal ILinuxAppServiceFileLogger FunctionsDetailsCategoryLogger => _functionsDetailsCategoryLogger;

        public static string TraceEventRegex { get; } = "(?<Level>[0-6]),(?<SubscriptionId>[^,]*),(?<HostName>[^,]*),(?<AppName>[^,]*),(?<FunctionName>[^,]*),(?<EventName>[^,]*),(?<Source>[^,]*),\"(?<Details>.*)\",\"(?<Summary>.*)\",(?<HostVersion>[^,]*),(?<EventTimestamp>[^,]+),(?<ExceptionType>[^,]*),\"(?<ExceptionMessage>.*)\",(?<FunctionInvocationId>[^,]*),(?<HostInstanceId>[^,]*),(?<ActivityId>[^,\"]*)";

        public static string MetricEventRegex { get; } = "(?<SubscriptionId>[^,]*),(?<AppName>[^,]*),(?<FunctionName>[^,]*),(?<EventName>[^,]*),(?<Average>\\d*),(?<Min>\\d*),(?<Max>\\d*),(?<Count>\\d*),(?<HostVersion>[^,]*),(?<EventTimestamp>[^,]+),(?<Details>[^,\"]*)";

        public static string DetailsEventRegex { get; } = "(?<AppName>[^,]*),(?<FunctionName>[^,]*),\"(?<InputBindings>.*)\",\"(?<OutputBindings>.*)\",(?<ScriptType>[^,]*),(?<IsDisabled>[0|1])";

        public static string AzureMonitorEventRegex { get; } = $"{ScriptConstants.LinuxAzureMonitorEventStreamName} (?<Level>[0-6]),(?<ResourceId>[^,]*),(?<OperationName>[^,]*),(?<Category>[^,]*),(?<RegionName>[^,]*),\"(?<Properties>[^,]*)\",(?<EventTimestamp>[^,]+)";

        public static string ExecutionEventRegex { get; } = "(?<executionId>[^,]*),(?<siteName>[^,]*),(?<concurrency>[^,]*),(?<functionName>[^,]*),(?<invocationId>[^,]*),(?<executionStage>[^,]*),(?<executionTimeSpan>[^,]*),(?<success>[^,]*),(?<dateTime>[^,]*)";

        public override void LogFunctionTraceEvent(LogLevel level, string subscriptionId, string appName, string functionName, string eventName,
            string source, string details, string summary, string exceptionType, string exceptionMessage,
            string functionInvocationId, string hostInstanceId, string activityId, string runtimeSiteName, string slotName, DateTime eventTimestamp)
        {
            var formattedEventTimestamp = eventTimestamp.ToString(EventTimestampFormat);
            var hostVersion = ScriptHost.Version;
            var hostName = _hostNameProvider.Value;
            using (FunctionsSystemLogsEventSource.SetActivityId(activityId))
            {
                WriteEvent(_functionsLogsCategoryLogger, $"{(int)ToEventLevel(level)},{subscriptionId},{hostName},{appName},{functionName},{eventName},{source},{NormalizeString(details)},{NormalizeString(summary)},{hostVersion},{formattedEventTimestamp},{exceptionType},{NormalizeString(exceptionMessage)},{functionInvocationId},{hostInstanceId},{activityId}");
            }
        }

        public override void LogFunctionMetricEvent(string subscriptionId, string appName, string functionName, string eventName, long average,
            long minimum, long maximum, long count, DateTime eventTimestamp, string data, string runtimeSiteName, string slotName)
        {
            var hostVersion = ScriptHost.Version;
            WriteEvent(_functionsMetricsCategoryLogger, $"{subscriptionId},{appName},{functionName},{eventName},{average},{minimum},{maximum},{count},{hostVersion},{eventTimestamp.ToString(EventTimestampFormat)},{data}");
        }

        public override void LogFunctionDetailsEvent(string siteName, string functionName, string inputBindings, string outputBindings,
            string scriptType, bool isDisabled)
        {
            WriteEvent(_functionsDetailsCategoryLogger, $"{siteName},{functionName},{NormalizeString(inputBindings)},{NormalizeString(outputBindings)},{scriptType},{(isDisabled ? 1 : 0)}");
        }

        public override void LogFunctionExecutionAggregateEvent(string siteName, string functionName, long executionTimeInMs,
            long functionStartedCount, long functionCompletedCount, long functionFailedCount)
        {
        }

        public override void LogFunctionExecutionEvent(string executionId, string siteName, int concurrency, string functionName,
            string invocationId, string executionStage, long executionTimeSpan, bool success)
        {
            string currentUtcTime = DateTime.UtcNow.ToString();
            bool detailedExecutionEventsDisabled = _functionsHostingConfigOptions.Value.DisableLinuxAppServiceExecutionDetails;
            if (!detailedExecutionEventsDisabled)
            {
                string log = string.Join(",", executionId, siteName, concurrency.ToString(), functionName, invocationId, executionStage, executionTimeSpan.ToString(), success.ToString(), currentUtcTime);
                WriteEvent(_functionsExecutionEventsCategoryLogger, log);
            }
            else
            {
                WriteEvent(_functionsExecutionEventsCategoryLogger, currentUtcTime);
            }
        }

        private static void WriteEvent(ILinuxAppServiceFileLogger logger, string evt)
        {
            logger.Log(evt);
        }

        private void WriteEvent(string eventPayload)
        {
            Console.WriteLine(eventPayload);
        }

        public override void LogAzureMonitorDiagnosticLogEvent(LogLevel level, string resourceId, string operationName, string category, string regionName, string properties)
        {
            _writeEvent($"{ScriptConstants.LinuxAzureMonitorEventStreamName} {(int)ToEventLevel(level)},{resourceId},{operationName},{category},{regionName},{NormalizeString(properties.Replace("'", string.Empty))},{DateTime.UtcNow}");
        }

        public static void LogUnhandledException(Exception e)
        {
            // Pipe the unhandled exception to stdout as part of docker logs.
            Console.WriteLine($"Unhandled exception on {DateTime.UtcNow}: {e?.ToString()}");
        }
    }
}
