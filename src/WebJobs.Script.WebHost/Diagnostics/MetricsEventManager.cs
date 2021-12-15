// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.ContainerManagement;
using Microsoft.Azure.WebJobs.Script.WebHost.Metrics;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public class MetricsEventManager : IDisposable
    {
        // Default time between flushes in seconds (every 30 seconds)
        private const int DefaultFlushIntervalMS = 30 * 1000;

        private readonly FunctionActivityTracker _functionActivityTracker = null;
        private readonly IEventGenerator _eventGenerator;
        private readonly int _functionActivityFlushIntervalSeconds;
        private readonly Timer _metricsFlushTimer;
        private readonly ILogger<MetricsEventManager> _logger;
        private bool _disposed;
        private AppServiceOptions _appServiceOptions;

        public MetricsEventManager(IOptionsMonitor<AppServiceOptions> appServiceOptionsMonitor, IEventGenerator generator, int functionActivityFlushIntervalSeconds, IMetricsPublisher metricsPublisher, ILinuxContainerActivityPublisher linuxContainerActivityPublisher, ILogger<MetricsEventManager> logger, int metricsFlushIntervalMS = DefaultFlushIntervalMS)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // we read these in the ctor (not static ctor) since it can change on the fly
            appServiceOptionsMonitor.OnChange(newOptions => _appServiceOptions = newOptions);
            _appServiceOptions = appServiceOptionsMonitor.CurrentValue;

            _eventGenerator = generator;
            _functionActivityFlushIntervalSeconds = functionActivityFlushIntervalSeconds;
            QueuedEvents = new ConcurrentDictionary<string, SystemMetricEvent>(StringComparer.OrdinalIgnoreCase);

            // Initialize the periodic log flush timer
            _metricsFlushTimer = new Timer(TimerFlush, null, metricsFlushIntervalMS, metricsFlushIntervalMS);

            _functionActivityTracker = new FunctionActivityTracker(appServiceOptionsMonitor, _eventGenerator, metricsPublisher, linuxContainerActivityPublisher, _functionActivityFlushIntervalSeconds, _logger);
        }

        /// <summary>
        /// Gets the collection of events that will be flushed on the next flush interval.
        /// </summary>
        public ConcurrentDictionary<string, SystemMetricEvent> QueuedEvents { get; }

        public object BeginEvent(string eventName, string functionName = null, string data = null)
        {
            ArgumentNullException.ThrowIfNull(eventName);

            return new SystemMetricEvent
            {
                FunctionName = functionName,
                EventName = eventName.ToLowerInvariant(),
                Timestamp = DateTime.UtcNow,
                StopWatch = ValueStopwatch.StartNew(),
                Data = data
            };
        }

        public void EndEvent(object eventHandle)
        {
            ArgumentNullException.ThrowIfNull(eventHandle);

            SystemMetricEvent evt = eventHandle as SystemMetricEvent;
            if (evt != null)
            {
                evt.Complete();
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
            ArgumentNullException.ThrowIfNull(eventName);

            eventName = Sanitizer.Sanitize(eventName);

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
            _functionActivityTracker.FunctionStarted(startedEvent);
        }

        internal void FunctionCompleted(FunctionStartedEvent completedEvent)
        {
            completedEvent.Complete();
            _functionActivityTracker.FunctionCompleted(completedEvent);
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
                    _appServiceOptions.AppName,
                    GetNormalizedString(function.Name),
                    function.Metadata != null ? SerializeBindings(function.Metadata.InputBindings) : GetNormalizedString(null),
                    function.Metadata != null ? SerializeBindings(function.Metadata.OutputBindings) : GetNormalizedString(null),
                    function.Metadata.Language,
                    function.Metadata != null ? function.Metadata.IsDisabled() : false);
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
                return string.Join(",", bindings.Select(b => b.Type));
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

        public void Flush()
        {
            _functionActivityTracker.Flush();

            FlushMetrics();
        }

        private void FlushMetrics()
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

        /// <summary>
        /// Flush any queued events to event source immediately.
        /// </summary>
        /// <remarks>This method may run concurrently with itself so ensure there are no
        /// unintended side effects or race conditions within the implementation.</remarks>
        protected internal virtual void TimerFlush(object state)
        {
            FlushMetrics();
        }

        protected internal virtual void WriteMetricEvents(SystemMetricEvent[] metricEvents)
        {
            ArgumentNullException.ThrowIfNull(metricEvents);

            AppServiceOptions currentAppServiceOptions = _appServiceOptions;
            foreach (SystemMetricEvent metricEvent in metricEvents)
            {
                _eventGenerator.LogFunctionMetricEvent(
                    currentAppServiceOptions.SubscriptionId,
                    currentAppServiceOptions.AppName,
                    metricEvent.FunctionName ?? string.Empty,
                    metricEvent.EventName.ToLowerInvariant(),
                    metricEvent.Average,
                    metricEvent.Minimum,
                    metricEvent.Maximum,
                    metricEvent.Count,
                    metricEvent.Timestamp,
                    metricEvent.Data ?? string.Empty,
                    currentAppServiceOptions.RuntimeSiteName ?? string.Empty,
                    currentAppServiceOptions.SlotName ?? string.Empty);
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            try
            {
                _logger.LogDebug($"Disposing {nameof(MetricsEventManager)}");
            }
            catch
            {
                // Best effort logging.
            }

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

                    if (_functionActivityTracker != null)
                    {
                        _functionActivityTracker.StopTimerAndRaiseFinishedEvent();
                        _functionActivityTracker.Dispose();
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
            // this interval should stay at 1 second because the timer is also
            // used to emit events every Nth second
            private const int _activityTimerIntervalMS = 1000;

            private readonly IMetricsPublisher _metricsPublisher;
            private readonly ILinuxContainerActivityPublisher _linuxContainerActivityPublisher;
            private readonly Timer _activityTimer;
            private readonly ILogger<MetricsEventManager> _logger;

            private ulong _totalExecutionCount = 0;
            private int _activeFunctionCount = 0;
            private int _functionActivityFlushInterval;
            private Dictionary<string, FunctionMetricSummary> _functionMetricsSummary = new Dictionary<string, FunctionMetricSummary>();
            private ConcurrentDictionary<Guid, FunctionStartedEvent> _runningFunctions = new ConcurrentDictionary<Guid, FunctionStartedEvent>();
            private bool _disposed = false;
            private AppServiceOptions _appServiceOptions;
            private int _activityFlushCounter;

            // This ID is just an event grouping mechanism that can be used by event consumers
            // to group events coming from the same app host.
            private string _executionId = Guid.NewGuid().ToString();

            internal FunctionActivityTracker(IOptionsMonitor<AppServiceOptions> appServiceOptionsMonitor, IEventGenerator generator, IMetricsPublisher metricsPublisher, ILinuxContainerActivityPublisher linuxContainerActivityPublisher, int functionActivityFlushInterval, ILogger<MetricsEventManager> logger)
            {
                MetricsEventGenerator = generator;
                appServiceOptionsMonitor.OnChange(newOptions => _appServiceOptions = newOptions);
                _appServiceOptions = appServiceOptionsMonitor.CurrentValue;
                _functionActivityFlushInterval = functionActivityFlushInterval;

                if (linuxContainerActivityPublisher != null && linuxContainerActivityPublisher != NullLinuxContainerActivityPublisher.Instance)
                {
                    _linuxContainerActivityPublisher = linuxContainerActivityPublisher;
                }

                if (metricsPublisher != null && metricsPublisher != NullMetricsPublisher.Instance)
                {
                    _metricsPublisher = metricsPublisher;
                }

                _activityFlushCounter = _functionActivityFlushInterval;
                _activityTimer = new Timer(TimerFlush, null, _activityTimerIntervalMS, _activityTimerIntervalMS);

                _logger = logger;
            }

            internal IEventGenerator MetricsEventGenerator { get; private set; }

            private void TimerFlush(object state)
            {
                try
                {
                    // we raise these events every interval as needed
                    RaiseMetricsPerFunctionEvent();

                    // only raise these events every Nth interval
                    if (_activityFlushCounter >= _functionActivityFlushInterval)
                    {
                        RaiseFunctionMetricEvents();
                        _activityFlushCounter = 0;
                    }
                    else
                    {
                        _activityFlushCounter += 1;
                    }
                }
                catch (Exception ex)
                {
                    // log error and continue
                    _logger.LogError(ex, "Error occurred when logging function activity");
                }
            }

            protected virtual void Dispose(bool disposing)
            {
                if (!_disposed)
                {
                    if (disposing)
                    {
                        _activityTimer?.Dispose();
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
                Interlocked.Increment(ref _activeFunctionCount);

                IncrementMetrics(startedEvent.FunctionMetadata.Name, ExecutionStage.Started, 0);
                _runningFunctions.TryAdd(startedEvent.InvocationId, startedEvent);
            }

            internal void FunctionCompleted(FunctionStartedEvent startedEvent)
            {
                Interlocked.Decrement(ref _activeFunctionCount);

                var functionStage = !startedEvent.Success ? ExecutionStage.Failed : ExecutionStage.Succeeded;
                long executionTimeInMS = (long)startedEvent.Duration.TotalMilliseconds;
                IncrementMetrics(startedEvent.FunctionMetadata.Name, functionStage, executionTimeInMS);

                RaiseFunctionMetricEvent(startedEvent, _activeFunctionCount, DateTime.UtcNow);
                _runningFunctions.TryRemove(startedEvent.InvocationId, out _);
            }

            internal void IncrementMetrics(string functionName, ExecutionStage stage, long executionTime)
            {
                lock (_functionMetricsSummary)
                {
                    CollectionsMarshal.GetValueRefOrAddDefault(_functionMetricsSummary, functionName, out _).Increment(stage, executionTime);
                }
            }

            internal void StopTimerAndRaiseFinishedEvent()
            {
                // stop the timer if it has been started
                _activityTimer?.Change(Timeout.Infinite, Timeout.Infinite);

                RaiseMetricsPerFunctionEvent();
            }

            internal void Flush()
            {
                RaiseMetricsPerFunctionEvent();
                RaiseFunctionMetricEvents();
            }

            /// <summary>
            /// Raise events for all current in progress functions
            /// </summary>
            private void RaiseFunctionMetricEvents()
            {
                if (_runningFunctions.IsEmpty)
                {
                    return;
                }

                // We only need to raise events here for functions that aren't completed.
                // Events are raised immediately for completed functions elsewhere.
                // Loop through and prune any completed runs
                // Also collects the currently running functions in the same enumeration to minimize cost.
                var running = new List<FunctionStartedEvent>();
                foreach (var possiblyRunning in _runningFunctions)
                {
                    if (possiblyRunning.Value.Completed)
                    {
                        _runningFunctions.TryRemove(possiblyRunning.Key, out _);
                    }
                    else
                    {
                        running.Add(possiblyRunning.Value);
                    }
                }

                var concurrency = running.Count;

                // We calculate concurrency here based on count, since these events are raised
                // on a background thread, so we want the actual count for this interval, not
                // the current count.
                var currentTime = DateTime.UtcNow;
                foreach (var runningFunction in running)
                {
                    RaiseFunctionMetricEvent(runningFunction, concurrency, currentTime);
                }
            }

            private void RaiseFunctionMetricEvent(FunctionStartedEvent runningFunctionInfo, int concurrency, DateTime currentTime)
            {
                double executionTimespan = 0;
                ExecutionStage executionStage;
                if (!runningFunctionInfo.Completed)
                {
                    executionStage = ExecutionStage.InProgress;
                    executionTimespan = (currentTime - runningFunctionInfo.Timestamp).TotalMilliseconds;
                }
                else
                {
                    // regardless of the actual Failed/Succeeded status, we always raise the final event
                    // with stage Finished
                    executionStage = ExecutionStage.Finished;
                    executionTimespan = runningFunctionInfo.Duration.TotalMilliseconds;
                }

                // Don't allocate the GUID string twice, though we can probably optimize this further upstream.
                var invocationId = runningFunctionInfo.InvocationId.ToString();

                MetricsEventGenerator.LogFunctionExecutionEvent(
                    _executionId,
                    _appServiceOptions.AppName,
                    concurrency,
                    runningFunctionInfo.FunctionMetadata.Name,
                    invocationId,
                    executionStage.ToString(),
                    (long)executionTimespan,
                    runningFunctionInfo.Success);

                if (_metricsPublisher != null)
                {
                    _metricsPublisher.AddFunctionExecutionActivity(
                        runningFunctionInfo.FunctionMetadata.Name,
                        invocationId,
                        concurrency,
                        executionStage.ToString(),
                        runningFunctionInfo.Success,
                        (long)executionTimespan,
                        _executionId,
                        currentTime,
                        runningFunctionInfo.Timestamp);
                }

                if (_linuxContainerActivityPublisher != null)
                {
                    var triggerType = runningFunctionInfo.FunctionMetadata.Trigger?.Type;
                    var activity = new ContainerFunctionExecutionActivity(DateTime.UtcNow, runningFunctionInfo.FunctionMetadata.Name,
                        executionStage, triggerType,
                        runningFunctionInfo.Success);
                    _linuxContainerActivityPublisher.PublishFunctionExecutionActivity(activity);
                }
            }

            private void RaiseMetricsPerFunctionEvent()
            {
                // Snapshot existing metrics by swapping out the collection we're building.
                // New metrics rolling in will go to the new collection while we aggregate the snapshot here.
                var summarySnapshot = Interlocked.Exchange(ref _functionMetricsSummary, new Dictionary<string, FunctionMetricSummary>());

                foreach (var function in summarySnapshot)
                {
                    var summary = function.Value;
                    MetricsEventGenerator.LogFunctionExecutionAggregateEvent(_appServiceOptions.AppName, function.Key, summary.TotalExectionTimeInMs, summary.StartedCount, summary.SucceededCount, summary.FailedCount);
                }
            }

            /// <summary>
            /// This evil mutable struct is for keeping metrics *as we go*, rather than collecting events and summarizing on the timer interval.
            /// In the aggregate: it's a much faster and lower allocation way of tracking the same numbers.
            /// </summary>
            private struct FunctionMetricSummary
            {
                public long StartedCount;
                public long FailedCount;
                public long SucceededCount;
                public long TotalExectionTimeInMs;

                public void Increment(ExecutionStage stage, long totalTime)
                {
                    switch (stage)
                    {
                        case ExecutionStage.Started:
                            StartedCount++;
                            break;
                        case ExecutionStage.Failed:
                            FailedCount++;
                            break;
                        case ExecutionStage.Succeeded:
                            SucceededCount++;
                            break;
                    }
                    TotalExectionTimeInMs += totalTime;
                }
            }
        }
    }
}