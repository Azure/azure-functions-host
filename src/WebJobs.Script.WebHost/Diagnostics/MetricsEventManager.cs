// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Diagnostics.Tracing;
using WebJobs.Script.WebHost.Models;

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

        public static void HostStarted(ScriptHost scriptHost)
        {
            if (scriptHost == null || scriptHost.Functions == null)
            {
                return;
            }

            var siteName = GetNormalizedString(Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME"));
            foreach (var function in scriptHost.Functions)
            {
                if (function == null || function.Metadata == null)
                {
                    continue;
                }

                MetricEventSource.Log.RaiseFunctionsInfoEvent(
                    siteName,
                    GetNormalizedString(function.Name),
                    function.Metadata != null
                        ? SerializeBindings(function.Metadata.InputBindings)
                        : GetNormalizedString(null),
                    function.Metadata != null
                        ? SerializeBindings(function.Metadata.OutputBindings) 
                        : GetNormalizedString(null),
                    function.Metadata.ScriptType.ToString(),
                    function.Metadata != null ? function.Metadata.IsDisabled : false);
            }
        }

        private static string SerializeBindings(IEnumerable<BindingMetadata> bindings)
        {
            if (bindings != null)
            {
                return string.Join(",", bindings.ToList().Select(b => b.Type.ToString()));
            }
            else
            {
                return GetNormalizedString(null);
            }
        }

        private static string GetNormalizedString(string input)
        {
            return input ?? string.Empty;
        }

        private class FunctionActivityTracker : IDisposable
        {
            private readonly string executionId = Guid.NewGuid().ToString();
            private DateTime startTime = DateTime.UtcNow;
            private ulong totalExecutionCount = 0;
            private ulong runningFunctionCount = 0;
            private const int MetricEventIntervalInSeconds = 5;
            private CancellationTokenSource etwTaskcancellationSource = new CancellationTokenSource();
            private ConcurrentQueue<FunctionMetrics> functionMetricsQueue = new ConcurrentQueue<FunctionMetrics>();
            private string siteName;

            internal FunctionActivityTracker()
            {
                siteName = GetNormalizedString(Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME"));

                Task.Run(
                    async () =>
                    {
                        try
                        {
                            int currentSecond = MetricEventIntervalInSeconds;
                            while (!etwTaskcancellationSource.Token.IsCancellationRequested)
                            {
                                RaiseMetricsPerFunctionEvent();
                                currentSecond = currentSecond + 1;
                                if (currentSecond >= MetricEventIntervalInSeconds)
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

                var metricEventPerFunction = new FunctionMetrics(startedEvent.FunctionMetadata.Name, ExecutionStage.Started, 0);
                functionMetricsQueue.Enqueue(metricEventPerFunction);
            }

            internal void FunctionCompleted(FunctionStartedEvent completedEvent)
            {
                if (runningFunctionCount > 0)
                {
                    runningFunctionCount--;
                }

                var functionStage = (completedEvent.Success == false) ? ExecutionStage.Failed : ExecutionStage.Succeeded;
                long executionTimeInMS = (long)completedEvent.EndTime.Subtract(completedEvent.StartTime).TotalMilliseconds;

                var monitoringEvent = new FunctionMetrics(completedEvent.FunctionMetadata.Name, functionStage, executionTimeInMS);
                functionMetricsQueue.Enqueue(monitoringEvent);
            }

            internal void StopEtwTaskAndRaiseFinishedEvent()
            {
                etwTaskcancellationSource.Cancel();
                RaiseMetricsPerFunctionEvent();
                RaiseMetricEtwEvent(ExecutionStage.Finished);
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

            private void RaiseMetricsPerFunctionEvent()
            {
                List<FunctionMetrics> metricsEventsList = GetMetricsQueueSnapshot();

                var aggregatedEventsPerFunction = from item in metricsEventsList
                                                  group item by item.FunctionName into FunctionGroups
                                                  select new
                                                  {
                                                      FunctionName = FunctionGroups.Key,
                                                      StartedCount = Convert.ToUInt64(FunctionGroups.Count(x => x.ExecutionStage == ExecutionStage.Started)),
                                                      FailedCount = Convert.ToUInt64(FunctionGroups.Count(x => x.ExecutionStage == ExecutionStage.Failed)),
                                                      SucceededCount = Convert.ToUInt64(FunctionGroups.Count(x => x.ExecutionStage == ExecutionStage.Succeeded)),
                                                      TotalExectionTimeInMs = Convert.ToUInt64(FunctionGroups.Sum(x => Convert.ToDecimal(x.ExecutionTimeInMS)))
                                                  };
                
                foreach (var functionEvent in aggregatedEventsPerFunction)
                {
                    MetricEventSource.Log.RaiseMetricsPerFunctionEvent(siteName, functionEvent.FunctionName, functionEvent.TotalExectionTimeInMs, functionEvent.StartedCount, functionEvent.SucceededCount, functionEvent.FailedCount);
                }
            }

            private List<FunctionMetrics> GetMetricsQueueSnapshot()
            {
                var queueSnapshot = new List<FunctionMetrics>();
                var currentQueueLength = functionMetricsQueue.Count;

                for (int iterator = 0; iterator < currentQueueLength; iterator++)
                {
                    FunctionMetrics queueItem;
                    if (functionMetricsQueue.TryDequeue(out queueItem))
                    {
                        queueSnapshot.Add(queueItem);
                    }
                }
                return queueSnapshot;
            }
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
            public void RaiseMetricsPerFunctionEvent(string siteName, string functionName, ulong executionTimeInMs, ulong functionStartedCount, ulong functionCompletedCount, ulong functionFailedCount)
            {
                if (IsEnabled())
                {
                    WriteEvent(57907, siteName, functionName, executionTimeInMs, functionStartedCount, functionCompletedCount, functionFailedCount);
                }
            }

            [Event(57908, Level = EventLevel.Informational, Channel = EventChannel.Operational)]
            public void RaiseFunctionsInfoEvent(string siteName, string functionName, string inputBindings, string outputBindings, string scriptType, bool isDisabled)
            {
                if (IsEnabled())
                {
                    WriteEvent(57908, siteName, functionName, inputBindings, outputBindings, scriptType, isDisabled);
                }
            }
        }
    }
}