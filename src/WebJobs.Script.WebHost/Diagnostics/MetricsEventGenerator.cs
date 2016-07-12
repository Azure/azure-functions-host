// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Diagnostics.Tracing;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    internal class MetricsEventGenerator : IMetricsEventGenerator
    {
        public void RaiseMetricsPerFunctionEvent(string siteName, string functionName, long executionTimeInMs, long functionStartedCount, long functionCompletedCount, long functionFailedCount)
        {
            MetricEventSource.Log.RaiseMetricsPerFunctionEvent(siteName, functionName, (ulong)executionTimeInMs, (ulong)functionStartedCount, (ulong)functionCompletedCount, (ulong)functionFailedCount);
        }

        public void RaiseFunctionsInfoEvent(string siteName, string functionName, string inputBindings, string outputBindings, string scriptType, bool isDisabled)
        {
            MetricEventSource.Log.RaiseFunctionsInfoEvent(siteName, functionName, inputBindings, outputBindings, scriptType, isDisabled);
        }

        public void RaiseFunctionExecutionEvent(string executionId, string siteName, int concurrency, string functionName, string invocationId, string executionStage, long executionTimeSpan, bool success)
        {
            MetricEventSource.Log.RaiseFunctionExecutionEvent(executionId, siteName, concurrency, functionName, invocationId, executionStage, (ulong)executionTimeSpan, success);
        }

        [EventSource(Guid = "08D0D743-5C24-43F9-9723-98277CEA5F9B")]
        private sealed class MetricEventSource : EventSource
        {
            internal static readonly MetricEventSource Log = new MetricEventSource();

            [Event(57907, Level = EventLevel.Informational, Channel = EventChannel.Operational)]
            public void RaiseMetricsPerFunctionEvent(string siteName, string functionName, ulong executionTimeInMs, ulong functionStartedCount, ulong functionCompletedCount, ulong functionFailedCount)
            {
                if (IsEnabled())
                {
                    WriteEvent(57907, siteName, functionName, executionTimeInMs, functionStartedCount, functionCompletedCount, functionFailedCount);
                }
            }

            [Event(57908, Level = EventLevel.Informational, Channel = EventChannel.Operational)]
            public void RaiseFunctionsInfoEvent(string siteName, string functionName, string inputBindings, string outputBindings, string scriptType, bool isDisabled)
            {
                if (IsEnabled())
                {
                    WriteEvent(57908, siteName, functionName, inputBindings, outputBindings, scriptType, isDisabled);
                }
            }

            [Event(57909, Level = EventLevel.Informational, Channel = EventChannel.Operational)]
            public void RaiseFunctionExecutionEvent(string executionId, string siteName, int concurrency, string functionName, string invocationId, string executionStage, ulong executionTimeSpan, bool success)
            {
                if (IsEnabled())
                {
                    WriteEvent(57909, executionId, siteName, concurrency, functionName, invocationId, executionStage, executionTimeSpan, success);
                }
            }
        }
    }
}