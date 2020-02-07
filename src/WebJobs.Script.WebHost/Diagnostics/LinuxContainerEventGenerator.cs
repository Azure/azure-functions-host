// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    internal class LinuxContainerEventGenerator : LinuxEventGenerator
    {
        private const int MaxDetailsLength = 10000;
        private readonly Action<string> _writeEvent;
        private readonly bool _consoleEnabled = true;
        private readonly IEnvironment _environment;
        private string _containerName;
        private string _stampName;
        private string _tenantId;

        public LinuxContainerEventGenerator(IEnvironment environment, Action<string> writeEvent = null)
        {
            _writeEvent = writeEvent ?? ConsoleWriter;
            _environment = environment;
            if (Environment.GetEnvironmentVariable(EnvironmentSettingNames.ConsoleLoggingDisabled) == "1")
            {
                _consoleEnabled = false;
            }
            _containerName = _environment.GetEnvironmentVariable(EnvironmentSettingNames.ContainerName)?.ToUpperInvariant();
        }

        // Note: the strange escaping of backslashes in these expressions for string literals (e.g. '\\\\\"') is because
        // of the current JSON serialization our log messages undergoe.
        public static string TraceEventRegex { get; } = $"{ScriptConstants.LinuxLogEventStreamName} (?<Level>[0-6]),(?<SubscriptionId>[^,]*),(?<AppName>[^,]*),(?<FunctionName>[^,]*),(?<EventName>[^,]*),(?<Source>[^,]*),\\\\\"(?<Details>.*)\\\\\",\\\\\"(?<Summary>.*)\\\\\",(?<HostVersion>[^,]*),(?<EventTimestamp>[^,]+),(?<ExceptionType>[^,]*),\\\\\"(?<ExceptionMessage>.*)\\\\\",(?<FunctionInvocationId>[^,]*),(?<HostInstanceId>[^,]*),(?<ActivityId>[^,\"]*),(?<ContainerName>[^,\"]*),(?<StampName>[^,\"]*),(?<TenantId>[^,\"]*)";

        public static string MetricEventRegex { get; } = $"{ScriptConstants.LinuxMetricEventStreamName} (?<SubscriptionId>[^,]*),(?<AppName>[^,]*),(?<FunctionName>[^,]*),(?<EventName>[^,]*),(?<Average>\\d*),(?<Min>\\d*),(?<Max>\\d*),(?<Count>\\d*),(?<HostVersion>[^,]*),(?<EventTimestamp>[^,]+),\\\\\"(?<Data>.*)\\\\\",(?<ContainerName>[^,\"]*),(?<StampName>[^,\"]*),(?<TenantId>[^,\"]*)";

        public static string DetailsEventRegex { get; } = $"{ScriptConstants.LinuxFunctionDetailsEventStreamName} (?<AppName>[^,]*),(?<FunctionName>[^,]*),\\\\\"(?<InputBindings>.*)\\\\\",\\\\\"(?<OutputBindings>.*)\\\\\",(?<ScriptType>[^,]*),(?<IsDisabled>[0|1])";

        public static string AzureMonitorEventRegex { get; } = $"{ScriptConstants.LinuxAzureMonitorEventStreamName} (?<Level>[0-6]),(?<ResourceId>[^,]*),(?<OperationName>[^,]*),(?<Category>[^,]*),(?<RegionName>[^,]*),\"(?<Properties>[^,]*)\",(?<ContainerName>[^,\"]*),(?<TenantId>[^,\"]*),(?<EventTimestamp>[^,]+)";

        private string StampName
        {
            get
            {
                if (string.IsNullOrEmpty(_stampName))
                {
                    _stampName = _environment.GetEnvironmentVariable(EnvironmentSettingNames.WebSiteHomeStampName)?.ToLowerInvariant();
                }
                return _stampName;
            }
        }

        private string TenantId
        {
            get
            {
                if (string.IsNullOrEmpty(_tenantId))
                {
                    _tenantId = _environment.GetEnvironmentVariable(EnvironmentSettingNames.WebSiteStampDeploymentId)?.ToLowerInvariant();
                }
                return _tenantId;
            }
        }

        public override void LogFunctionTraceEvent(LogLevel level, string subscriptionId, string appName, string functionName, string eventName, string source, string details, string summary, string exceptionType, string exceptionMessage, string functionInvocationId, string hostInstanceId, string activityId, string runtimeSiteName, string slotName, DateTime eventTimestamp)
        {
            string formattedEventTimeStamp = eventTimestamp.ToString(EventTimestampFormat);
            string hostVersion = ScriptHost.Version;
            FunctionsSystemLogsEventSource.Instance.SetActivityId(activityId);
            details = details.Length > MaxDetailsLength ? details.Substring(0, MaxDetailsLength) : details;

            _writeEvent($"{ScriptConstants.LinuxLogEventStreamName} {(int)ToEventLevel(level)},{subscriptionId},{appName},{functionName},{eventName},{source},{NormalizeString(details)},{NormalizeString(summary)},{hostVersion},{formattedEventTimeStamp},{exceptionType},{NormalizeString(exceptionMessage)},{functionInvocationId},{hostInstanceId},{activityId},{_containerName},{StampName},{TenantId}");
        }

        public override void LogFunctionMetricEvent(string subscriptionId, string appName, string functionName, string eventName, long average, long minimum, long maximum, long count, DateTime eventTimestamp, string data, string runtimeSiteName, string slotName)
        {
            string hostVersion = ScriptHost.Version;

            _writeEvent($"{ScriptConstants.LinuxMetricEventStreamName} {subscriptionId},{appName},{functionName},{eventName},{average},{minimum},{maximum},{count},{hostVersion},{eventTimestamp.ToString(EventTimestampFormat)},{NormalizeString(data)},{_containerName},{StampName},{TenantId}");
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
            _writeEvent($"{ScriptConstants.LinuxAzureMonitorEventStreamName} {(int)ToEventLevel(level)},{resourceId},{operationName},{category},{regionName},{NormalizeString(properties.Replace("'", string.Empty))},{_containerName},{TenantId},{DateTime.UtcNow.ToString()}");
        }

        public static void LogUnhandledException(Exception e)
        {
            var linuxContainerEventGenerator = new LinuxContainerEventGenerator(SystemEnvironment.Instance);
            linuxContainerEventGenerator.LogFunctionTraceEvent(LogLevel.Error,
                SystemEnvironment.Instance.GetSubscriptionId() ?? string.Empty,
                SystemEnvironment.Instance.GetAzureWebsiteUniqueSlotName() ?? string.Empty, string.Empty, string.Empty,
                nameof(LogUnhandledException), e?.ToString(), string.Empty, e?.GetType().ToString() ?? string.Empty,
                e?.ToString(), string.Empty, string.Empty, string.Empty,
                SystemEnvironment.Instance.GetRuntimeSiteName() ?? string.Empty,
                SystemEnvironment.Instance.GetSlotName() ?? string.Empty,
                DateTime.UtcNow);
        }
    }
}