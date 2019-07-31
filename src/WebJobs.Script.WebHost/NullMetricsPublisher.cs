// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.WebHost.Metrics;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class NullMetricsPublisher : IMetricsPublisher
    {
        private readonly ILogger _logger;

        public NullMetricsPublisher(ILogger<NullMetricsPublisher> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogDebug("Initializing null metrics publisher");
        }

        public void AddFunctionExecutionActivity(string functionName, string invocationId, int concurrency, string executionStage, bool success, long executionTimeSpan, string executionId, DateTime eventTimeStamp, DateTime functionStartTime)
        {
            _logger.LogDebug("Ignoring function activity metric: {functionName} {invocationId} {concurrency} {executionStage} {success} {executionTimeSpan} {executionId} {eventTimeStamp} {functionStartTime}",
                functionName, invocationId, concurrency, executionStage, success, executionTimeSpan, executionId, eventTimeStamp, functionStartTime);
        }
    }
}
