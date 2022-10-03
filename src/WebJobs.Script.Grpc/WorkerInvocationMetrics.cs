// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.Grpc
{
    public class WorkerInvocationMetrics
    {
        public int TotalInvocations { get; set; }

        public int SuccessfulInvocations { get; set; }

        public double AverageInvocationLatency { get; set; }

        public static int IncrementTotalInvocationsOfWorker(string workerId, IDictionary<string, WorkerInvocationMetrics> invocationMetricsPerWorkerId)
        {
            invocationMetricsPerWorkerId.TryGetValue(workerId, out WorkerInvocationMetrics workerInvocationParameters);

            if (workerInvocationParameters == null)
            {
                workerInvocationParameters = new WorkerInvocationMetrics();
            }

            workerInvocationParameters.TotalInvocations = workerInvocationParameters.TotalInvocations + 1;
            invocationMetricsPerWorkerId[workerId] = workerInvocationParameters;

            return workerInvocationParameters.TotalInvocations;
        }

        public static int IncrementSuccessfulInvocationsOfWorker(string workerId, IDictionary<string, WorkerInvocationMetrics> invocationMetricsPerWorkerId)
        {
            invocationMetricsPerWorkerId.TryGetValue(workerId, out WorkerInvocationMetrics workerInvocationParameters);

            if (workerInvocationParameters == null)
            {
                workerInvocationParameters = new WorkerInvocationMetrics();
            }

            workerInvocationParameters.SuccessfulInvocations = workerInvocationParameters.SuccessfulInvocations + 1;
            invocationMetricsPerWorkerId[workerId] = workerInvocationParameters;

            return workerInvocationParameters.SuccessfulInvocations;
        }

        public static double UpdateAverageInvocationLatency(string workerId, IDictionary<string, WorkerInvocationMetrics> invocationMetricsPerWorkerId, TimeSpan currentInvocationLatency)
        {
            invocationMetricsPerWorkerId.TryGetValue(workerId, out WorkerInvocationMetrics workerInvocationParameters);

            if (workerInvocationParameters == null)
            {
                workerInvocationParameters = new WorkerInvocationMetrics();
            }

            workerInvocationParameters.AverageInvocationLatency = ((workerInvocationParameters.AverageInvocationLatency * (workerInvocationParameters.TotalInvocations - 1)) + currentInvocationLatency.TotalMilliseconds) / workerInvocationParameters.TotalInvocations;
            invocationMetricsPerWorkerId[workerId] = workerInvocationParameters;

            return workerInvocationParameters.AverageInvocationLatency;
        }
    }
}
