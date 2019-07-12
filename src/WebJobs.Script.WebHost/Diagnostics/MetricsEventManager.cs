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
using Microsoft.Azure.WebJobs.Script.WebHost.Metrics;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public class MetricsEventManager : IDisposable
    {
        // Default time between flushes in seconds (every 30 seconds)
        private const int DefaultFlushIntervalMS = 30 * 1000;

        private static FunctionActivityTracker instance = null;
        private readonly IEventGenerator _eventGenerator;
        private readonly int _functionActivityFlushIntervalSeconds;
        private readonly Timer _metricsFlushTimer;
        private readonly object _functionActivityTrackerLockObject = new object();
        private static string appName;
        private static string subscriptionId;
        private bool _disposed;
        private IMetricsPublisher _metricsPublisher;

        public MetricsEventManager(IEnvironment environment, IEventGenerator generator, int functionActivityFlushIntervalSeconds, IMetricsPublisher metricsPublisher, int metricsFlushIntervalMS = DefaultFlushIntervalMS)
        {
            // we read these in the ctor (not static ctor) since it can change on the fly
            appName = GetNormalizedString(environment.GetAzureWebsiteUniqueSlotName());
            subscriptionId = environment.GetSubscriptionId() ?? string.Empty;

            _eventGenerator = generator;
            _functionActivityFlushIntervalSeconds = functionActivityFlushIntervalSeconds;
            QueuedEvents = new ConcurrentDictionary<string, SystemMetricEvent>(StringComparer.OrdinalIgnoreCase);

            // Initialize the periodic log flush timer
            _metricsFlushTimer = new Timer(TimerFlush, null, metricsFlushIntervalMS, metricsFlushIntervalMS);

            _metricsPublisher = metricsPublisher;
        }

        /// <summary>
        /// Gets the collection of events that will be flushed on the next flush interval.
        /// </summary>
        public ConcurrentDictionary<string, SystemMetricEvent> QueuedEvents { get; }

        public object BeginEvent(string eventName, string functionName = null, string data = null)
        {
            if (string.IsNullOrEmpty(eventName))
            {
                throw new ArgumentNullException(nameof(eventName));
            }

            return new SystemMetricEvent
            {
                FunctionName = functionName,
                EventName = eventName.ToLowerInvariant(),
                Timestamp = DateTime.UtcNow,
                Data = data
            };
        }

        public void EndEvent(object eventHandle)
        {
            if (eventHandle == null)
            {
                throw new ArgumentNullException(nameof(eventHandle));
            }

            SystemMetricEvent evt = eventHandle as SystemMetricEvent;
            if (evt != null)
            {
                evt.Duration = DateTime.UtcNow - evt.Timestamp;
                long latencyMS = (long)evt.Duration.TotalMilliseconds;

                // event aggregation is based on this key
                // for each unique key, there will be only 1
                // queued event that we aggregate into
                string key = GetAggregateKey(evt.EventName, evt.FunctionName);

                QueuedEvents.AddOrUpdate(key,
                    (name) =>
                    {
                        // create the default event that will be added
                        // if an event isn't already queued for this key
                        return new SystemMetricEvent
                        {
                            FunctionName = evt.FunctionName,
                            EventName = evt.EventName,
                            Minimum = latencyMS,
                            Maximum = latencyMS,
                            Average = latencyMS,
                            Count = 1,
                            Data = evt.Data
                        };
                    },
                    (name, evtToUpdate) =>
                    {
                        // Aggregate into the existing event
                        // While we'll be performing an aggregation later,
                        // we retain the count so weighted averages can be performed
                        evtToUpdate.Maximum = Math.Max(evtToUpdate.Maximum, latencyMS);
                        evtToUpdate.Minimum = Math.Min(evtToUpdate.Minimum, latencyMS);
                        evtToUpdate.Average += latencyMS;  // the average is calculated later - for now we sum
                        evtToUpdate.Count++;

                        return evtToUpdate;
                    });
            }
        }

        public void LogEvent(string eventName, string functionName = null, string data = null)
        {
            if (string.IsNullOrEmpty(eventName))
            {
                throw new ArgumentNullException(nameof(eventName));
            }

            string key = GetAggregateKey(eventName, functionName);
            QueuedEvents.AddOrUpdate(key,
                (name) =>
                {
                    // create the default event that will be added
                    // if an event isn't already queued for this key
                    return new SystemMetricEvent
                    {
                        FunctionName = functionName,
                        EventName = eventName.ToLowerInvariant(),
                        Count = 1,
                        Data = data
                    };
                },
                (name, evtToUpdate) =>
                {
                    // update the existing event
                    evtToUpdate.Count++;

                    return evtToUpdate;
                });
        }

        internal void FunctionStarted(FunctionStartedEvent startedEvent)
        {
            lock (_functionActivityTrackerLockObject)
            {
                if (instance == null)
                {
                    instance = new FunctionActivityTracker(_eventGenerator, _metricsPublisher, _functionActivityFlushIntervalSeconds);
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
                        instance.Dispose();
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

                _eventGenerator.LogFunctionDetailsEvent(
                    appName,
                    GetNormalizedString(function.Name),
                    function.Metadata != null ? SerializeBindings(function.Metadata.InputBindings) : GetNormalizedString(null),
                    function.Metadata != null ? SerializeBindings(function.Metadata.OutputBindings) : GetNormalizedString(null),
                    function.Metadata.Language,
                    function.Metadata != null ? function.Metadata.IsDisabled : false);
            }
        }

        /// <summary>
        /// Constructs the aggregate key used to group events. When metric events are
        /// added for later aggregation on flush, they'll be grouped by this key.
        /// </summary>
        internal static string GetAggregateKey(string eventName, string functionName = null)
        {
            string key = string.IsNullOrEmpty(functionName) ?
                eventName : $"{eventName}_{functionName}";

            return key.ToLowerInvariant();
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

        /// <summary>
        /// Flush any queued events to event source immediately.
        /// </summary>
        /// <remarks>This method may run concurrently with itself so ensure there are no
        /// unintended side effects or race conditions within the implementation.</remarks>
        protected internal virtual void TimerFlush(object state)
        {
            if (QueuedEvents.Count == 0)
            {
                return;
            }

            SystemMetricEvent[] eventsToFlush = QueuedEvents.Values.ToArray();
            QueuedEvents.Clear();

            // Use the same timestamp for all events. Since these are
            // aggregated events, individual timestamps for when the events were
            // started are meaningless
            DateTime eventTimestamp = DateTime.UtcNow;

            foreach (SystemMetricEvent evt in eventsToFlush)
            {
                evt.Timestamp = eventTimestamp;

                // perform the average calculation that we have postponed
                evt.Average /= evt.Count;
            }

            WriteMetricEvents(eventsToFlush);
        }

        protected internal virtual void WriteMetricEvents(SystemMetricEvent[] metricEvents)
        {
            if (metricEvents == null)
            {
                throw new ArgumentNullException(nameof(metricEvents));
            }

            foreach (SystemMetricEvent metricEvent in metricEvents)
            {
                _eventGenerator.LogFunctionMetricEvent(
                    subscriptionId,
                    appName,
                    metricEvent.FunctionName ?? string.Empty,
                    metricEvent.EventName.ToLowerInvariant(),
                    metricEvent.Average,
                    metricEvent.Minimum,
                    metricEvent.Maximum,
                    metricEvent.Count,
                    metricEvent.Timestamp,
                    metricEvent.Data ?? string.Empty);
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // flush any outstanding events
                    TimerFlush(state: null);

                    if (_metricsFlushTimer != null)
                    {
                        _metricsFlushTimer.Dispose();
                    }
                }

                _disposed = true;
            }
        }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private class FunctionActivityTracker : IDisposable
        {
            private readonly string _executionId = Guid.NewGuid().ToString();
            private readonly object _functionMetricEventLockObject = new object();
            private ulong _totalExecutionCount = 0;
            private int _functionActivityFlushInterval;
            private CancellationTokenSource _etwTaskCancellationSource = new CancellationTokenSource();
            private ConcurrentQueue<FunctionMetrics> _functionMetricsQueue = new ConcurrentQueue<FunctionMetrics>();
            private Dictionary<string, RunningFunctionInfo> _runningFunctions = new Dictionary<string, RunningFunctionInfo>();
            private bool _disposed = false;
            private IMetricsPublisher _metricsPublisher;

            internal FunctionActivityTracker(IEventGenerator generator, IMetricsPublisher metricsPublisher, int functionActivityFlushInterval)
            {
                MetricsEventGenerator = generator;
                _functionActivityFlushInterval = functionActivityFlushInterval;
                _metricsPublisher = metricsPublisher;
                Task.Run(
                    async () =>
                    {
                        try
                        {
                            int currentSecond = _functionActivityFlushInterval;
                            while (!_etwTaskCancellationSource.Token.IsCancellationRequested)
                            {
                                RaiseMetricsPerFunctionEvent();

                                if (currentSecond >= _functionActivityFlushInterval)
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

            internal IEventGenerator MetricsEventGenerator { get; private set; }

            protected virtual void Dispose(bool disposing)
            {
                if (!_disposed)
                {
                    if (disposing)
                    {
                        _etwTaskCancellationSource.Dispose();
                    }
                    _disposed = true;
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
                            _runningFunctions.Add(key, new RunningFunctionInfo(startedEvent.FunctionMetadata.Name, startedEvent.InvocationId, startedEvent.Timestamp, startedEvent.Success));
                        }
                    }
                }
            }

            internal void FunctionCompleted(FunctionStartedEvent functionStartedEvent)
            {
                var functionStage = (functionStartedEvent.Success == false) ? ExecutionStage.Failed : ExecutionStage.Succeeded;
                long executionTimeInMS = (long)functionStartedEvent.Duration.TotalMilliseconds;

                var monitoringEvent = new FunctionMetrics(functionStartedEvent.FunctionMetadata.Name, functionStage, executionTimeInMS);
                _functionMetricsQueue.Enqueue(monitoringEvent);
                var key = GetDictionaryKey(functionStartedEvent.FunctionMetadata.Name, functionStartedEvent.InvocationId);
                if (_runningFunctions.ContainsKey(key))
                {
                    lock (_functionMetricEventLockObject)
                    {
                        if (_runningFunctions.ContainsKey(key))
                        {
                            var functionInfo = _runningFunctions[key];
                            functionInfo.ExecutionStage = ExecutionStage.Finished;
                            functionInfo.Success = functionStartedEvent.Success;

                            var endTime = functionStartedEvent.Timestamp + functionStartedEvent.Duration;
                            functionInfo.EndTime = functionStartedEvent.Timestamp + functionStartedEvent.Duration;

                            RaiseFunctionMetricEvent(functionInfo, _runningFunctions.Keys.Count, endTime);
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

                MetricsEventGenerator.LogFunctionExecutionEvent(
                    _executionId,
                    appName,
                    concurrency,
                    runningFunctionInfo.Name,
                    runningFunctionInfo.InvocationId.ToString(),
                    runningFunctionInfo.ExecutionStage.ToString(),
                    (long)executionTimespan,
                    runningFunctionInfo.Success);

                if (_metricsPublisher != null)
                {
                    _metricsPublisher.AddFunctionExecutionActivity(
                        runningFunctionInfo.Name,
                        runningFunctionInfo.InvocationId.ToString(),
                        concurrency,
                        runningFunctionInfo.ExecutionStage.ToString(),
                        runningFunctionInfo.Success,
                        (long)executionTimespan,
                        currentTime);
                }
            }

            private static string GetDictionaryKey(string name, Guid invocationId)
            {
                return string.Format("{0}_{1}", name.ToString(), invocationId.ToString());
            }

            private void RaiseMetricsPerFunctionEvent()
            {
                List<FunctionMetrics> metricsEventsList = GetMetricsQueueSnapshot();

                var aggregatedEventsPerFunction = from item in metricsEventsList
                                                  group item by item.FunctionName into functionGroups
                                                  select new
                                                  {
                                                      FunctionName = functionGroups.Key,
                                                      StartedCount = Convert.ToUInt64(functionGroups.Count(x => x.ExecutionStage == ExecutionStage.Started)),
                                                      FailedCount = Convert.ToUInt64(functionGroups.Count(x => x.ExecutionStage == ExecutionStage.Failed)),
                                                      SucceededCount = Convert.ToUInt64(functionGroups.Count(x => x.ExecutionStage == ExecutionStage.Succeeded)),
                                                      TotalExectionTimeInMs = Convert.ToUInt64(functionGroups.Sum(x => Convert.ToDecimal(x.ExecutionTimeInMS)))
                                                  };

                foreach (var functionEvent in aggregatedEventsPerFunction)
                {
                    MetricsEventGenerator.LogFunctionExecutionAggregateEvent(appName, functionEvent.FunctionName, (long)functionEvent.TotalExectionTimeInMs, (long)functionEvent.StartedCount, (long)functionEvent.SucceededCount, (long)functionEvent.FailedCount);
                }
            }

            private List<FunctionMetrics> GetMetricsQueueSnapshot()
            {
                var queueSnapshot = new List<FunctionMetrics>();
                var currentQueueLength = _functionMetricsQueue.Count;

                for (int iterator = 0; iterator < currentQueueLength; iterator++)
                {
                    if (_functionMetricsQueue.TryDequeue(out FunctionMetrics queueItem))
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
                    Name = name;
                    InvocationId = invocationId;
                    StartTime = startTime;
                    Success = success;
                    ExecutionStage = executionStage;
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