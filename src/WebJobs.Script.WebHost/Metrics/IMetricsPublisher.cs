// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Metrics
{
    public interface IMetricsPublisher
    {
        void AddFunctionExecutionActivity(string functionName, string invocationId, int concurrency, string executionStage, bool success, long executionTimeSpan, string executionId, DateTime eventTimeStamp, DateTime functionStartTime);

        void OnFunctionStarted(string functionName, string invocationId);

        void OnFunctionCompleted(string functionName, string invocationId);
    }
}