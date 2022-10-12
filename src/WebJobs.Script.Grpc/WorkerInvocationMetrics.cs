// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Concurrent;
using System.Threading;

namespace Microsoft.Azure.WebJobs.Script.Grpc
{
    internal class WorkerInvocationMetrics
    {
        public int TotalInvocations { get; set; }

        public int SuccessfulInvocations { get; set; }

        public double AverageInvocationLatency { get; set; }

        public static void IncrementTotalInvocationsOfWorker(string workerId, ConcurrentDictionary<string, WorkerInvocationMetrics> invocationMetricsPerWorkerId)
        {
            invocationMetricsPerWorkerId.AddOrUpdate(workerId, new WorkerInvocationMetrics().IncrementTotalInvocations(), (k, v) => v.IncrementTotalInvocations());
        }

        public static void IncrementSuccessfulInvocationsOfWorker(string workerId, ConcurrentDictionary<string, WorkerInvocationMetrics> invocationMetricsPerWorkerId)
        {
            invocationMetricsPerWorkerId.AddOrUpdate(workerId, new WorkerInvocationMetrics().IncrementSuccessfulInvocations(), (k, v) => v.IncrementSuccessfulInvocations());
        }
    }

    internal static class WorkerInvocationMetricsExtensions
    {
        public static WorkerInvocationMetrics IncrementTotalInvocations(this WorkerInvocationMetrics obj)
        {
            int currentCount = obj.TotalInvocations;
            obj.TotalInvocations = Interlocked.Increment(ref currentCount);
            return obj;
        }

        public static WorkerInvocationMetrics IncrementSuccessfulInvocations(this WorkerInvocationMetrics obj)
        {
            int currentCount = obj.SuccessfulInvocations;
            obj.SuccessfulInvocations = Interlocked.Increment(ref currentCount);
            return obj;
        }
    }
}
