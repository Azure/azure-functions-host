// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO.Abstractions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Metrics;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Metrics
{
    public sealed partial class FlexConsumptionMetricsPublisher : IMetricsPublisher, IDisposable
    {
        private readonly IOptionsMonitor<StandbyOptions> _standbyOptions;
        private readonly FlexConsumptionMetricsPublisherOptions _options;
        private readonly IEnvironment _environment;
        private readonly ILogger<FlexConsumptionMetricsPublisher> _logger;
        private readonly IHostMetricsProvider _metricsProvider;
        private readonly object _lock = new();
        private readonly IFileSystem _fileSystem;
        private readonly LegionMetricsFileManager _metricsFileManager;

        private DateTime _currentActivityIntervalStart;
        private DateTime _activityIntervalHighWatermark = DateTime.MinValue;
        private IDisposable _standbyOptionsOnChangeSubscription;
        private DateTime _lastPublishTime = DateTime.UtcNow;
        private Lifecycle _lifecycle;

        public FlexConsumptionMetricsPublisher(IEnvironment environment, IOptionsMonitor<StandbyOptions> standbyOptions, IOptions<FlexConsumptionMetricsPublisherOptions> options,
            ILogger<FlexConsumptionMetricsPublisher> logger, IFileSystem fileSystem, IHostMetricsProvider metricsProvider)
        {
            _standbyOptions = standbyOptions ?? throw new ArgumentNullException(nameof(standbyOptions));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _fileSystem = fileSystem ?? new FileSystem();
            _metricsFileManager = new LegionMetricsFileManager(_options.MetricsFilePath, _fileSystem, _logger, _options.MaxFileCount);
            _metricsProvider = metricsProvider ?? throw new ArgumentNullException(nameof(metricsProvider));

            if (_standbyOptions.CurrentValue.InStandbyMode)
            {
                _logger.LogDebug("Registering StandbyOptions change subscription.");
                _standbyOptionsOnChangeSubscription = _standbyOptions.OnChange(o => OnStandbyOptionsChange());
            }
            else
            {
                Start();
            }
        }

        // internal properties for testing
        internal long FunctionExecutionCount { get; set; }

        internal long FunctionExecutionTimeMS { get; set; }

        internal long ActiveFunctionCount { get; set; }

        internal bool IsAlwaysReady { get; set; }

        internal LegionMetricsFileManager MetricsFileManager => _metricsFileManager;

        private bool IsStarted => _lifecycle is not null;

        public void Start()
        {
            if (_lifecycle is not null)
            {
                return;
            }

            lock (_lock)
            {
                if (_lifecycle is not null)
                {
                    return;
                }

                IsAlwaysReady = _environment
                    .GetEnvironmentVariable(EnvironmentSettingNames.FunctionsAlwaysReadyInstance) == "1";

                _logger.LogInformation(
                    $"Starting metrics publisher (AlwaysReady={IsAlwaysReady},"
                    + $" MetricsPath='{_metricsFileManager.MetricsFilePath}').");

                _lifecycle = new(
                    this,
                    TimeSpan.FromMilliseconds(_options.InitialPublishDelayMS),
                    TimeSpan.FromMilliseconds(_options.MetricsPublishIntervalMS));
            }
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _lifecycle, null)?.Dispose();
            Interlocked.Exchange(ref _standbyOptionsOnChangeSubscription, null)?.Dispose();
        }

        internal async Task OnPublishMetrics(DateTime now, ValueStopwatch stopwatch)
        {
            try
            {
                lock (_lock)
                {
                    if (ActiveFunctionCount > 0)
                    {
                        // at the end of an interval, we'll meter any outstanding activity up to the end of the interval
                        MeterCurrentActiveInterval(now);
                    }
                }

                bool hasActivity = FunctionExecutionCount > 0 || FunctionExecutionTimeMS > 0 || _metricsProvider.HasMetrics();
                bool shouldForcePublish = (now - _lastPublishTime) >= TimeSpan.FromMilliseconds(_options.KeepAliveIntervalMS);

                if (!hasActivity && !shouldForcePublish && !IsAlwaysReady)
                {
                    // No activity and not time for keep-alive publish & not always ready
                    return;
                }

                // we've been accumulating function activity for the entire period
                // publish this activity and reset
                Metrics metrics = null;
                lock (_lock)
                {
                    metrics = new Metrics
                    {
                        TotalTimeMS = (long)stopwatch.GetElapsedTime().TotalMilliseconds,
                        ExecutionCount = FunctionExecutionCount,
                        ExecutionTimeMS = FunctionExecutionTimeMS,
                        IsAlwaysReady = IsAlwaysReady,
                        InstanceId = _metricsProvider.InstanceId,
                        FunctionGroup = _metricsProvider.FunctionGroup
                    };

                    var scaleMetrics = _metricsProvider.GetHostMetricsOrNull();
                    if (scaleMetrics is not null)
                    {
                        metrics.AppFailureCount = scaleMetrics.TryGetValue(HostMetrics.AppFailureCount, out long appFailureCount) ? appFailureCount : 0;
                        metrics.StartedInvocationCount = scaleMetrics.TryGetValue(HostMetrics.StartedInvocationCount, out long startedInvocationCount) ? startedInvocationCount : 0;
                        metrics.ActiveInvocationCount = scaleMetrics.TryGetValue(HostMetrics.ActiveInvocationCount, out long activeInvocationCount) ? activeInvocationCount : 0;
                    }

                    FunctionExecutionTimeMS = FunctionExecutionCount = 0;
                    _lastPublishTime = now;
                }

                await _metricsFileManager.PublishMetricsAsync(metrics);
            }
            catch (Exception ex) when (!ex.IsFatal())
            {
                // ensure no background exceptions escape
                _logger.LogError(ex, $"Error publishing metrics.");
            }
        }

        private void OnStandbyOptionsChange()
        {
            if (!_standbyOptions.CurrentValue.InStandbyMode)
            {
                Start();
            }
        }

        public void OnFunctionStarted(string functionName, string invocationId)
        {
            OnFunctionStarted(functionName, invocationId, DateTime.UtcNow);
        }

        internal void OnFunctionStarted(string functionName, string invocationId, DateTime now)
        {
            if (!IsStarted)
            {
                return;
            }

            lock (_lock)
            {
                if (ActiveFunctionCount == 0)
                {
                    // we're transitioning from inactive to active
                    _currentActivityIntervalStart = now;
                }

                ActiveFunctionCount++;
            }
        }

        public void OnFunctionCompleted(string functionName, string invocationId)
        {
            OnFunctionCompleted(functionName, invocationId, DateTime.UtcNow);
        }

        internal void OnFunctionCompleted(string functionName, string invocationId, DateTime now)
        {
            if (!IsStarted)
            {
                return;
            }

            lock (_lock)
            {
                if (ActiveFunctionCount > 0)
                {
                    ActiveFunctionCount--;
                }
                else
                {
                    // We got a completion event without a corresponding start.
                    // This might happen during specialization for example.
                    // Ignore the event.
                    return;
                }

                if (ActiveFunctionCount == 0)
                {
                    // We're transitioning from active to inactive, so we need to accumulate the elapsed time
                    // for this interval.
                    MeterCurrentActiveInterval(now);
                }

                // for every completed invocation, increment our invocation count
                FunctionExecutionCount++;
            }
        }

        public void AddFunctionExecutionActivity(string functionName, string invocationId, int concurrency, string executionStage, bool success, long executionTimeSpan, string executionId, DateTime eventTimeStamp, DateTime functionStartTime)
        {
            // nothing to do here - we only care about Started/Completed events.
        }

        private void MeterCurrentActiveInterval(DateTime now)
        {
            DateTime adjustedActivityIntervalStart = _currentActivityIntervalStart;
            if (_activityIntervalHighWatermark > _currentActivityIntervalStart)
            {
                // If we've already metered a previous interval past the current time,
                // we move forward (since we never want to meter the same interval twice).
                adjustedActivityIntervalStart = _activityIntervalHighWatermark;
            }

            // If the elapsed duration is negative, it means invocations are still before
            // the high watermark, so have already been metered.
            double elapsedMS = (now - adjustedActivityIntervalStart).TotalMilliseconds;
            if (elapsedMS > 0)
            {
                // Accumulate the duration for this interval, applying minimums and rounding
                var duration = Math.Max(elapsedMS, _options.MinimumActivityIntervalMS);
                duration = RoundUp(duration, _options.MetricsGranularityMS);
                FunctionExecutionTimeMS += (long)duration;

                // Move the high watermark timestamp forward to the point
                // up to which we've metered
                _activityIntervalHighWatermark = adjustedActivityIntervalStart.AddMilliseconds(duration);
            }
        }

        // Rounds up the given metric to a specified granularity. For example, RoundUp(1320.00, 100) = 1400, but RoundUp(1300.00, 100) = 1300.
        private static double RoundUp(double metric, int granularity)
        {
            return Math.Ceiling(metric / granularity) * granularity;
        }

        internal class Metrics
        {
            /// <summary>
            /// Gets or sets the total time for the metrics interval.
            /// </summary>
            public long TotalTimeMS { get; set; }

            /// <summary>
            /// Gets or sets the total time duration that the instance
            /// had function activity during the interval.
            /// </summary>
            public long ExecutionTimeMS { get; set; }

            /// <summary>
            /// Gets or sets the total number of functions invocations that
            /// completed during the interval.
            /// </summary>
            public long ExecutionCount { get; set; }

            /// <summary>
            /// Gets or sets a value indicating whether the instance is
            /// AlwaysReady.
            /// </summary>
            public bool IsAlwaysReady { get; set; }

            /// <summary>
            /// Gets or sets the instance Id.
            /// </summary>
            public string InstanceId { get; set; }

            /// <summary>
            /// Gets or sets the function group name. This can be either http, durable or
            /// the name of a function.
            /// </summary>
            public string FunctionGroup { get; set; }

            /// <summary>
            /// Gets or sets the total number of permanent host failures.
            /// </summary>
            public long AppFailureCount { get; set; }

            /// <summary>
            /// Gets or sets the total number of in-progress function invocations.
            /// </summary>
            public long ActiveInvocationCount { get; set; }

            /// <summary>
            /// Gets or sets the total number of function invocations that have started.
            /// </summary>
            public long StartedInvocationCount { get; set; }
        }
    }
}
