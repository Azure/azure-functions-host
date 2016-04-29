// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace WebJobs.Script.WebHost.Diagnostics
{
    public interface IMetricsEventGenerator
    {
        void RaiseFunctionsMetricEvent(string executionId, long executionTimeSpan, long executionCount, string executionStage);

        void RaiseMetricsPerFunctionEvent(string siteName, string functionName, long executionTimeInMs, long functionStartedCount, long functionCompletedCount, long functionFailedCount);

        void RaiseFunctionsInfoEvent(string siteName, string functionName, string inputBindings, string outputBindings, string scriptType, bool isDisabled);

        void RaiseFunctionExecutionEvent(string executionId, string siteName, int concurrency, string functionName, string invocationId, string executionStage, long executionTimeSpan, bool success);
    }
}
