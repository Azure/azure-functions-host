// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.Rpc
{
    public class WorkerInvocationMetricsTests
    {
        [Fact]
        public void Test_IncrementTotalInvocationsOfWorker_Success()
        {
            string workerId = "workerId1";
            ConcurrentDictionary<string, WorkerInvocationMetrics> invocationMetricsPerWorkerId = new ConcurrentDictionary<string, WorkerInvocationMetrics>();
            var workerInvocationMetrics = new WorkerInvocationMetrics();
            workerInvocationMetrics.TotalInvocations = 1;
            invocationMetricsPerWorkerId.TryAdd(workerId, workerInvocationMetrics);
            int totalInvocations = WorkerInvocationMetrics.IncrementTotalInvocationsOfWorker(workerId, invocationMetricsPerWorkerId);
            Assert.Equal(2, totalInvocations);
            Assert.Equal(2, invocationMetricsPerWorkerId[workerId].TotalInvocations);
        }

        [Fact]
        public void Test_IncrementSuccessfulInvocationsOfWorker_Success()
        {
            string workerId = "workerId1";
            ConcurrentDictionary<string, WorkerInvocationMetrics> invocationMetricsPerWorkerId = new ConcurrentDictionary<string, WorkerInvocationMetrics>();
            var workerInvocationMetrics = new WorkerInvocationMetrics();
            workerInvocationMetrics.SuccessfulInvocations = 1;
            invocationMetricsPerWorkerId.TryAdd(workerId, workerInvocationMetrics);
            int successfulInvocations = WorkerInvocationMetrics.IncrementSuccessfulInvocationsOfWorker(workerId, invocationMetricsPerWorkerId);
            Assert.Equal(2, successfulInvocations);
            Assert.Equal(2, invocationMetricsPerWorkerId[workerId].SuccessfulInvocations);
        }

        [Fact]
        public void Test_UpdateAverageInvocationLatency_Success()
        {
            string workerId = "workerId1";
            ConcurrentDictionary<string, WorkerInvocationMetrics> invocationMetricsPerWorkerId = new ConcurrentDictionary<string, WorkerInvocationMetrics>();
            var workerInvocationMetrics = new WorkerInvocationMetrics();
            workerInvocationMetrics.AverageInvocationLatency = 2000;
            workerInvocationMetrics.TotalInvocations = 2;
            invocationMetricsPerWorkerId.TryAdd(workerId, workerInvocationMetrics);
            double averageInvocationLatency = WorkerInvocationMetrics.UpdateAverageInvocationLatency(workerId, invocationMetricsPerWorkerId, new TimeSpan(0, 0, 1));
            Assert.Equal(1500, averageInvocationLatency);
            Assert.Equal(1500, invocationMetricsPerWorkerId[workerId].AverageInvocationLatency);
        }
    }
}
