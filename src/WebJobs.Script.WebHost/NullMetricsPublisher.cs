// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using Microsoft.Azure.WebJobs.Script.Metrics;
using Microsoft.Azure.WebJobs.Script.WebHost.Metrics;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class NullMetricsPublisher : IMetricsPublisher, IWorkerInvocationTracker
    {
        private static readonly Lazy<NullMetricsPublisher> _instance = new Lazy<NullMetricsPublisher>(new NullMetricsPublisher());

        private NullMetricsPublisher()
        {
        }

        public static IMetricsPublisher Instance => _instance.Value;

        public void AddFunctionExecutionActivity(string functionName, string invocationId, int concurrency, string executionStage, bool success, long executionTimeSpan, string executionId, DateTime eventTimeStamp, DateTime functionStartTime)
        {
        }

        public void OnFunctionStarted(string functionName, string invocationId)
        {
        }

        public void OnFunctionCompleted(string functionName, string invocationId)
        {
        }

        /// <inheritdoc />
        public void WorkerLinked(string workerId)
        {
        }

        /// <inheritdoc />
        public void WorkerUnlinked(string workerId)
        {
        }

        /// <inheritdoc />
        public bool TryDispatchInvocation(string invocationId, string workerId, CancellationToken cancellationToken, Func<bool> dispatch)
        {
            ArgumentNullException.ThrowIfNull(dispatch);

            return dispatch();
        }
    }
}
