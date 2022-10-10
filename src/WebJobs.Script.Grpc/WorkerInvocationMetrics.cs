// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.Grpc
{
    internal class WorkerInvocationMetrics
    {
        public int TotalInvocations { get; set; }

        public int SuccessfulInvocations { get; set; }

        public double AverageInvocationLatency { get; set; }

        public static int IncrementTotalInvocationsOfWorker(string workerId, ConcurrentDictionary<string, WorkerInvocationMetrics> invocationMetricsPerWorkerId)
        {
            invocationMetricsPerWorkerId.TryGetValue(workerId, out WorkerInvocationMetrics workerInvocationMetrics);
            workerInvocationMetrics = workerInvocationMetrics ?? new WorkerInvocationMetrics();

            workerInvocationMetrics.TotalInvocations = workerInvocationMetrics.TotalInvocations + 1;
            invocationMetricsPerWorkerId[workerId] = workerInvocationMetrics;

            return workerInvocationMetrics.TotalInvocations;
        }

        public static int IncrementSuccessfulInvocationsOfWorker(string workerId, ConcurrentDictionary<string, WorkerInvocationMetrics> invocationMetricsPerWorkerId)
        {
            invocationMetricsPerWorkerId.TryGetValue(workerId, out WorkerInvocationMetrics workerInvocationMetrics);
            workerInvocationMetrics = workerInvocationMetrics ?? new WorkerInvocationMetrics();

            workerInvocationMetrics.SuccessfulInvocations = workerInvocationMetrics.SuccessfulInvocations + 1;
            invocationMetricsPerWorkerId[workerId] = workerInvocationMetrics;

            return workerInvocationMetrics.SuccessfulInvocations;
        }
    }
}
