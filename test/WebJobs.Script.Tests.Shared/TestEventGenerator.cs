﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class TestEventGenerator : IEventGenerator
    {
        private List<FunctionTraceEvent> _functionTraceEvents = new List<FunctionTraceEvent>();
        private object _syncLock = new object();

        public IEnumerable<FunctionTraceEvent> GetFunctionTraceEvents()
        {
            lock (_syncLock)
            {
                return _functionTraceEvents.ToList();
            }
        }

        public void ClearEvents()
        {
            lock (_syncLock)
            {
                _functionTraceEvents.Clear();
            }
        }

        public void LogAzureMonitorDiagnosticLogEvent(LogLevel level, string resourceId, string operationName, string category, string regionName, string properties)
        {
        }

        public void LogFunctionDetailsEvent(string siteName, string functionName, string inputBindings, string outputBindings, string scriptType, bool isDisabled)
        {
        }

        public void LogFunctionExecutionAggregateEvent(string siteName, string functionName, long executionTimeInMs, long functionStartedCount, long functionCompletedCount, long functionFailedCount)
        {
        }

        public void LogFunctionExecutionEvent(string executionId, string siteName, int concurrency, string functionName, string invocationId, string executionStage, long executionTimeSpan, bool success)
        {
        }

        public void LogFunctionMetricEvent(string subscriptionId, string appName, string functionName, string eventName, long average, long minimum, long maximum, long count, DateTime eventTimestamp, string data)
        {
        }

        public void LogFunctionTraceEvent(LogLevel level, string subscriptionId, string appName, string functionName, string eventName, string source, string details, string summary, string exceptionType, string exceptionMessage, string functionInvocationId, string hostInstanceId, string activityId)
        {
            FunctionTraceEvent traceEvent = new FunctionTraceEvent
            {
                Timestamp = DateTimeOffset.UtcNow,
                Level = level,
                SubscriptionId = subscriptionId,
                AppName = appName,
                FunctionName = functionName,
                EventName = eventName,
                Source = source,
                Details = details,
                Summary = summary,
                ExceptionType = exceptionType,
                ExceptionMessage = exceptionMessage,
                FunctionInvocationId = functionInvocationId,
                HostInstanceId = hostInstanceId,
                ActivityId = activityId
            };

            lock (_syncLock)
            {
                _functionTraceEvents.Add(traceEvent);
            }
        }
    }
}
