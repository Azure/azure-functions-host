// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Diagnostics.Extensions;
using Microsoft.Azure.WebJobs.Script.Metrics;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Metrics
{
    public class FlexConsumptionMetricsPublisher : IMetricsPublisher, IDisposable
    {
        private readonly IOptionsMonitor<StandbyOptions> _standbyOptions;
        private readonly FlexConsumptionMetricsPublisherOptions _options;
        private readonly IEnvironment _environment;
        private readonly ILogger<FlexConsumptionMetricsPublisher> _logger;
        private readonly IHostMetricsProvider _metricsProvider;
        private readonly object _lock = new object();
        private readonly IFileSystem _fileSystem;
        private readonly LegionMetricsFileManager _metricsFileManager;

        private Timer _metricsPublisherTimer;
        private bool _started = false;
        private DateTime _currentActivityIntervalStart;
        private DateTime _activityIntervalHighWatermark = DateTime.MinValue;
        private ValueStopwatch _intervalStopwatch;
        private IDisposable _standbyOptionsOnChangeSubscription;
        private TimeSpan _metricPublishInterval;
        private TimeSpan _initialPublishDelay;

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

        public void Start()
        {
            Initialize();

            _logger.LogInformation($"Starting metrics publisher (AlwaysReady={IsAlwaysReady}, MetricsPath='{_metricsFileManager.MetricsFilePath}').");

            _metricsPublisherTimer = new Timer(OnFunctionMetricsPublishTimer, null, _initialPublishDelay, _metricPublishInterval);
            _started = true;
        }

        /// <summary>
        /// Initialize any environmentally derived state after specialization, prior to starting the publisher.
        /// </summary>
        internal void Initialize()
        {
            _metricPublishInterval = TimeSpan.FromMilliseconds(_options.MetricsPublishIntervalMS);
            _initialPublishDelay = TimeSpan.FromMilliseconds(_options.InitialPublishDelayMS);
            _intervalStopwatch = ValueStopwatch.StartNew();

            IsAlwaysReady = _environment.GetEnvironmentVariable(EnvironmentSettingNames.FunctionsAlwaysReadyInstance) == "1";
        }

        internal async Task OnPublishMetrics(DateTime now)
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

                if (FunctionExecutionCount == 0 && FunctionExecutionTimeMS == 0 && !IsAlwaysReady && !_metricsProvider.HasMetrics())
                {
                    // no activity to report
                    return;
                }

                // we've been accumulating function activity for the entire period
                // publish this activity and reset
                Metrics metrics = null;
                lock (_lock)
                {
                    metrics = new Metrics
                    {
                        TotalTimeMS = (long)_intervalStopwatch.GetElapsedTime().TotalMilliseconds,
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
                }

                await _metricsFileManager.PublishMetricsAsync(metrics);
            }
            catch (Exception ex) when (!ex.IsFatal())
            {
                // ensure no background exceptions escape
                _logger.LogError(ex, $"Error publishing metrics.");
            }
            finally
            {
                _intervalStopwatch = ValueStopwatch.StartNew();
            }
        }

        private async void OnFunctionMetricsPublishTimer(object state)
        {
            await OnPublishMetrics(DateTime.UtcNow);
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
            if (!_started)
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
            if (!_started)
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

        public void Dispose()
        {
            _metricsPublisherTimer?.Dispose();
            _metricsPublisherTimer = null;

            _standbyOptionsOnChangeSubscription?.Dispose();
            _standbyOptionsOnChangeSubscription = null;
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
