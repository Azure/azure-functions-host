// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Diagnostics.Tracing;

namespace WebJobs.Script.WebHost.Diagnostics
{
    public static class MetricsEventManager
    {        
        private static FunctionActivityTracker instance = null;

        private static object functionActivityTrackerLockObject = new object();

        public static void FunctionStarted(FunctionStartedEvent startedEvent)
        {
            lock (functionActivityTrackerLockObject)
            {
                if (instance == null)
                {
                    instance = new FunctionActivityTracker();
                }
                instance.FunctionStarted(startedEvent);
            }
        }

        public static void FunctionCompleted(FunctionStartedEvent completedEvent)
        {
            lock (functionActivityTrackerLockObject)
            {
                if (instance != null)
                {
                    instance.FunctionCompleted(completedEvent);
                    if (!instance.IsActive)
                    {
                        instance.StopEtwTaskAndRaiseFinishedEvent();
                        instance = null;
                    }
                }
            }
        }

        private class FunctionActivityTracker : IDisposable
        {
            private readonly string executionId = Guid.NewGuid().ToString();
            private DateTime startTime = DateTime.UtcNow;
            private ulong totalExecutionCount = 0;
            private ulong runningFunctionCount = 0;
            private CancellationTokenSource etwTaskcancellationSource = new CancellationTokenSource();
            private ConcurrentQueue<FunctionMonitoringMetrics> functionMonitoringStatsQueue = new ConcurrentQueue<FunctionMonitoringMetrics>();

            internal FunctionActivityTracker()
            {
                int metricEventIntervalInSeconds = 5;
                Task.Run(
                    async () =>
                    {
                        try
                        {
                            int currentSecond = metricEventIntervalInSeconds;
                            while (!etwTaskcancellationSource.Token.IsCancellationRequested)
                            {
                                RaiseMonitoringEvent();

                                currentSecond = currentSecond + 1;
                                if (currentSecond >= metricEventIntervalInSeconds)
                                {
                                    RaiseMetricEtwEvent(ExecutionStage.InProgress);
                                    currentSecond = 0;
                                }

                                await Task.Delay(TimeSpan.FromSeconds(1), etwTaskcancellationSource.Token);
                            }
                        }
                        catch (TaskCanceledException)
                        {
                            // This exception gets throws when cancellation request is raised via cancellation token.
                            // Let's eat this exception and continue
                        }
                    },
                    etwTaskcancellationSource.Token);
            }

            public enum ExecutionStage
            {
                InProgress,
                Finished,
                Started,
                Failed,
                Succedded
            }

            internal bool IsActive
            {
                get
                {
                    return runningFunctionCount != 0;
                }
            }

            protected virtual void Dispose(bool disposing)
            {
                if (disposing)
                {                    
                    etwTaskcancellationSource.Dispose();
                }
            }

            public void Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }

           internal void FunctionStarted(FunctionStartedEvent startedEvent)
           {
                totalExecutionCount++;
                runningFunctionCount++;

                var monitoringEvent = new FunctionMonitoringMetrics(startedEvent.FunctionMetadata.Name, ExecutionStage.Started, 0);
                functionMonitoringStatsQueue.Enqueue(monitoringEvent);
            }

            internal void FunctionCompleted(FunctionStartedEvent completedEvent)
            {
                if (runningFunctionCount > 0)
                {
                    runningFunctionCount--;
                }

                var functionStage = (completedEvent.Success == false) ? ExecutionStage.Failed : ExecutionStage.Succedded;
                ulong executionTimeInMs = (ulong)completedEvent.EndTime.Subtract(completedEvent.StartTime).TotalMilliseconds;

                var monitoringEvent = new FunctionMonitoringMetrics(completedEvent.FunctionMetadata.Name, functionStage, executionTimeInMs);
                functionMonitoringStatsQueue.Enqueue(monitoringEvent);
            }

            internal void StopEtwTaskAndRaiseFinishedEvent()
            {
                etwTaskcancellationSource.Cancel();
                RaiseMetricEtwEvent(ExecutionStage.Finished);
                RaiseMonitoringEvent();
            }

            private void RaiseMetricEtwEvent(ExecutionStage executionStage)
            {
                var timeSpan = (ulong)(DateTime.UtcNow - startTime).TotalMilliseconds;
                var executionCount = totalExecutionCount;
                WriteFunctionsMetricEvent(executionId, timeSpan, executionCount, executionStage.ToString());
            }

            private static void WriteFunctionsMetricEvent(string executionId, ulong executionTimeSpan, ulong executionCount, string executionStage)
            {
                MetricEventSource.Log.RaiseFunctionsMetricEvent(executionId, executionTimeSpan, executionCount, executionStage);
            }

            private void RaiseMonitoringEvent()
            {
                List<FunctionMonitoringMetrics> monitoringEventsList = GetMonitoringQueueSnapshot();
                
                var aggregatedEventsPerFunction = from item in monitoringEventsList
                                                  group item by new { item.FunctionName } into FunctionGroups
                                                  select new
                                                  {
                                                      FunctionName = FunctionGroups.Key.FunctionName,
                                                      StartedCount = Convert.ToUInt64(FunctionGroups.Count(x => x.ExecutionStage == ExecutionStage.Started)),
                                                      FailedCount = Convert.ToUInt64(FunctionGroups.Count(x => x.ExecutionStage == ExecutionStage.Failed)),
                                                      SucceddedCount = Convert.ToUInt64(FunctionGroups.Count(x => x.ExecutionStage == ExecutionStage.Succedded)),
                                                      TotalExectionTimeInMs = Convert.ToUInt64(FunctionGroups.Sum(x => Convert.ToDecimal(x.ExecutionTimeInMs)))
                                                  };

                var siteName = Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME");
                foreach (var functionEvent in aggregatedEventsPerFunction)
                {
                    MetricEventSource.Log.RaiseFunctionsMonitoringEvent(siteName, functionEvent.FunctionName, functionEvent.TotalExectionTimeInMs, functionEvent.StartedCount, functionEvent.SucceddedCount, functionEvent.FailedCount);
                }
            }

            private List<FunctionMonitoringMetrics> GetMonitoringQueueSnapshot()
            {
                var queueSnapshot = new List<FunctionMonitoringMetrics>();
                var currentQueueLength = functionMonitoringStatsQueue.Count;

                for (int iterator = 0; iterator < currentQueueLength; iterator++)
                {
                    FunctionMonitoringMetrics queueItem;
                    if (functionMonitoringStatsQueue.TryDequeue(out queueItem))
                    {
                        queueSnapshot.Add(queueItem);
                    }
                }
                return queueSnapshot;
            }
            
            [EventSource(Guid = "08D0D743-5C24-43F9-9723-98277CEA5F9B")]
            private sealed class MetricEventSource : EventSource
            {
                internal static readonly MetricEventSource Log = new MetricEventSource();

                [Event(57906, Level = EventLevel.Informational, Channel = EventChannel.Operational)]
                public void RaiseFunctionsMetricEvent(string executionId, ulong executionTimeSpan, ulong executionCount, string executionStage)
                {
                    if (IsEnabled())
                    {
                        WriteEvent(57906, executionId, executionTimeSpan, executionCount, executionStage);
                    }
                }

                [Event(57907, Level = EventLevel.Informational, Channel = EventChannel.Operational)]
                public void RaiseFunctionsMonitoringEvent(string siteName, string functionName, ulong executionTimeInMs, ulong functionStartedCount, ulong functionCompletedCount, ulong functionFailedCount)
                {
                    if (IsEnabled())
                    {
                        WriteEvent(57907, siteName, functionName, executionTimeInMs, functionStartedCount, functionCompletedCount, functionFailedCount);
                    }
                }
            }

            public class FunctionMonitoringMetrics
            {
                private string _functionName;
                private ExecutionStage _executionStage;
                private ulong _executionTimeInMs;

                public FunctionMonitoringMetrics(string functionName, ExecutionStage executionStage, ulong executionTimeInMs)
                {
                    _functionName = functionName;
                    _executionStage = executionStage;
                    _executionTimeInMs = executionTimeInMs;
                }

                public string FunctionName
                {
                    get
                    {
                        return _functionName;
                    }
                }

                public ExecutionStage ExecutionStage
                {
                    get
                    {
                        return _executionStage;
                    }
                }

                public ulong ExecutionTimeInMs
                {
                    get
                    {
                        return _executionTimeInMs;
                    }
                }
            }
        }
    }
}