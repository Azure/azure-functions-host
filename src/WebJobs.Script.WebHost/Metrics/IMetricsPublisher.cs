// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Metrics
{
    public interface IMetricsPublisher
    {
        void PublishFunctionExecutionActivity(string functionName, string invocationId, int concurrency, string executionStage, bool success, long executionTimeSpan);

        void Start(string containerName, Action<LogLevel, string, string> logMetricsPublishEvent);
    }
}