// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    internal class MetricsEventManager
    {        
        private static FunctionActivityTracker instance = null;
        private object _functionActivityTrackerLockObject = new object();
        private IMetricsEventGenerator _metricsEventGenerator;
        private int _metricEventIntervalInSeconds;
        private static string siteName;

        internal MetricsEventManager(IMetricsEventGenerator generator, int metricEventIntervalInSeconds)
        {
            // we read this in ctor (not static ctor) since it can change on the fly
            siteName = GetNormalizedString(Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME"));

            _metricsEventGenerator = generator;
            _metricEventIntervalInSeconds = metricEventIntervalInSeconds;
        }

        internal void FunctionStarted(FunctionStartedEvent startedEvent)
        {
            lock (_functionActivityTrackerLockObject)
            {
                if (instance == null)
                {
                    instance = new FunctionActivityTracker(_metricsEventGenerator, _metricEventIntervalInSeconds);
                }
                instance.FunctionStarted(startedEvent);
            }
        }

        internal void FunctionCompleted(FunctionStartedEvent completedEvent)
        {
            lock (_functionActivityTrackerLockObject)
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

        internal void HostStarted(ScriptHost scriptHost)
        {
            if (scriptHost == null || scriptHost.Functions == null)
            {
                return;
            }

            foreach (var function in scriptHost.Functions)
            {
                if (function == null || function.Metadata == null)
                {
                    continue;
                }

                _metricsEventGenerator.RaiseFunctionsInfoEvent(
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
            private readonly string _executionId = Guid.NewGuid().ToString();
            private readonly object _functionMetricEventLockObject = new object();
            private ulong _totalExecutionCount = 0;
            private int _metricEventIntervalInSeconds;
            private CancellationTokenSource _etwTaskCancellationSource = new CancellationTokenSource();
            private ConcurrentQueue<FunctionMetrics> _functionMetricsQueue = new ConcurrentQueue<FunctionMetrics>();
            private Dictionary<string, RunningFunctionInfo> _runningFunctions = new Dictionary<string, RunningFunctionInfo>();

            internal FunctionActivityTracker(IMetricsEventGenerator generator, int metricEventIntervalInSeconds)
            {
                MetricsEventGenerator = generator;
                _metricEventIntervalInSeconds = metricEventIntervalInSeconds;
                Task.Run(
                    async () =>
                    {
                        try
                        {
                            int currentSecond = _metricEventIntervalInSeconds;
                            while (!_etwTaskCancellationSource.Token.IsCancellationRequested)
                            {
                                RaiseMetricsPerFunctionEvent();
                                
                                if (currentSecond >= _metricEventIntervalInSeconds)
                                {
                                    RaiseFunctionMetricEvents();
                                    currentSecond = 0;
                                }
                                else
                                {
                                    currentSecond = currentSecond + 1;
                                }

                                await Task.Delay(TimeSpan.FromSeconds(1), _etwTaskCancellationSource.Token);
                            }
                        }
                        catch (TaskCanceledException)
                        {
                            // This exception gets throws when cancellation request is raised via cancellation token.
                            // Let's eat this exception and continue
                        }
                    },
                    _etwTaskCancellationSource.Token);
            }
            
            internal bool IsActive
            {
                get
                {
                    return _runningFunctions.Count != 0;
                }
            }

            internal IMetricsEventGenerator MetricsEventGenerator { get; private set; }            

            protected virtual void Dispose(bool disposing)
            {
                if (disposing)
                {                    
                    _etwTaskCancellationSource.Dispose();
                }
            }

            public void Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }

            internal void FunctionStarted(FunctionStartedEvent startedEvent)
            {
                _totalExecutionCount++;

                var metricEventPerFunction = new FunctionMetrics(startedEvent.FunctionMetadata.Name, ExecutionStage.Started, 0);
                _functionMetricsQueue.Enqueue(metricEventPerFunction);
                var key = GetDictionaryKey(startedEvent.FunctionMetadata.Name, startedEvent.InvocationId);
                if (!_runningFunctions.ContainsKey(key))
                {
                    lock (_functionMetricEventLockObject)
                    {
                        if (!_runningFunctions.ContainsKey(key))
                        {
                            _runningFunctions.Add(key, new RunningFunctionInfo(startedEvent.FunctionMetadata.Name, startedEvent.InvocationId, startedEvent.StartTime, startedEvent.Success));
                        }
                    }
                }
            }

            internal void FunctionCompleted(FunctionStartedEvent completedEvent)
            {
                var functionStage = (completedEvent.Success == false) ? ExecutionStage.Failed : ExecutionStage.Succeeded;
                long executionTimeInMS = (long)completedEvent.EndTime.Subtract(completedEvent.StartTime).TotalMilliseconds;

                var monitoringEvent = new FunctionMetrics(completedEvent.FunctionMetadata.Name, functionStage, executionTimeInMS);
                _functionMetricsQueue.Enqueue(monitoringEvent);
                var key = GetDictionaryKey(completedEvent.FunctionMetadata.Name, completedEvent.InvocationId);
                if (_runningFunctions.ContainsKey(key))
                {
                    lock (_functionMetricEventLockObject)
                    {
                        if (_runningFunctions.ContainsKey(key))
                        {
                            var functionInfo = _runningFunctions[key];
                            functionInfo.ExecutionStage = ExecutionStage.Finished;
                            functionInfo.Success = completedEvent.Success;
                            functionInfo.EndTime = completedEvent.EndTime;
                            RaiseFunctionMetricEvent(functionInfo, _runningFunctions.Keys.Count, completedEvent.EndTime);
                            _runningFunctions.Remove(key);
                        }
                    }
                }
            }

            internal void StopEtwTaskAndRaiseFinishedEvent()
            {
                _etwTaskCancellationSource.Cancel();
                RaiseMetricsPerFunctionEvent();
            }

            private void RaiseFunctionMetricEvents()
            {
                lock (_functionMetricEventLockObject)
                {
                    var currentTime = DateTime.UtcNow;
                    foreach (var runningFunctionPair in _runningFunctions)
                    {
                        var runningFunctionInfo = runningFunctionPair.Value;
                        RaiseFunctionMetricEvent(runningFunctionInfo, _runningFunctions.Keys.Count, currentTime);
                    }
                }
            }

            private void RaiseFunctionMetricEvent(RunningFunctionInfo runningFunctionInfo, int concurrency, DateTime currentTime)
            {
                double executionTimespan = 0;
                if (runningFunctionInfo.ExecutionStage == ExecutionStage.Finished)
                {
                    executionTimespan = (runningFunctionInfo.EndTime - runningFunctionInfo.StartTime).TotalMilliseconds;
                }
                else
                {
                    executionTimespan = (currentTime - runningFunctionInfo.StartTime).TotalMilliseconds;
                }

                MetricsEventGenerator.RaiseFunctionExecutionEvent(
                    _executionId,
                    siteName,
                    concurrency,
                    runningFunctionInfo.Name,
                    runningFunctionInfo.InvocationId.ToString(),
                    runningFunctionInfo.ExecutionStage.ToString(),
                    (long)executionTimespan,
                    runningFunctionInfo.Success);
            }

            private static string GetDictionaryKey(string name, Guid invocationId)
            {
                return string.Format("{0}_{1}", name.ToString(), invocationId.ToString());
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
                    MetricsEventGenerator.RaiseMetricsPerFunctionEvent(siteName, functionEvent.FunctionName, (long)functionEvent.TotalExectionTimeInMs, (long)functionEvent.StartedCount, (long)functionEvent.SucceededCount, (long)functionEvent.FailedCount);
                }
            }

            private List<FunctionMetrics> GetMetricsQueueSnapshot()
            {
                var queueSnapshot = new List<FunctionMetrics>();
                var currentQueueLength = _functionMetricsQueue.Count;

                for (int iterator = 0; iterator < currentQueueLength; iterator++)
                {
                    FunctionMetrics queueItem;
                    if (_functionMetricsQueue.TryDequeue(out queueItem))
                    {
                        queueSnapshot.Add(queueItem);
                    }
                }

                return queueSnapshot;
            }

            private class RunningFunctionInfo
            {                
                public RunningFunctionInfo(string name, Guid invocationId, DateTime startTime, bool success, ExecutionStage executionStage = ExecutionStage.InProgress)
                {
                    this.Name = name;
                    this.InvocationId = invocationId;
                    this.StartTime = startTime;
                    this.Success = success;
                    this.ExecutionStage = executionStage;
                }

                public string Name { get; private set; }
                public Guid InvocationId { get; private set; }
                public DateTime StartTime { get; private set; }
                public ExecutionStage ExecutionStage { get; set; }
                public DateTime EndTime { get; set; }
                public bool Success { get; set; }
            }
        }
    }
}