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
        private readonly object _lock = new object();
        private readonly IFileSystem _fileSystem;

        private Timer _metricsPublisherTimer;
        private bool _started = false;
        private ValueStopwatch _instanceActivityStopwatch;
        private ValueStopwatch _intervalStopwatch;
        private IDisposable _standbyOptionsOnChangeSubscription;
        private TimeSpan _metricPublishInterval;
        private TimeSpan _initialPublishDelay;

        public FlexConsumptionMetricsPublisher(IEnvironment environment, IOptionsMonitor<StandbyOptions> standbyOptions, IOptions<FlexConsumptionMetricsPublisherOptions> options, ILogger<FlexConsumptionMetricsPublisher> logger, IFileSystem fileSystem)
        {
            _standbyOptions = standbyOptions ?? throw new ArgumentNullException(nameof(standbyOptions));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _fileSystem = fileSystem ?? new FileSystem();

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

        internal string MetricsFilePath { get; set; }

        public void Start()
        {
            Initialize();

            _logger.LogInformation($"Starting metrics publisher (AlwaysReady={IsAlwaysReady}, MetricsPath='{MetricsFilePath}').");

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
            MetricsFilePath = _options.MetricsFilePath;

            IsAlwaysReady = _environment.GetEnvironmentVariable(EnvironmentSettingNames.FunctionsAlwaysReadyInstance) == "1";
        }

        internal async Task OnPublishMetrics()
        {
            try
            {
                if (FunctionExecutionCount == 0 && FunctionExecutionTimeMS == 0 && !IsAlwaysReady)
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
                        IsAlwaysReady = IsAlwaysReady
                    };

                    FunctionExecutionTimeMS = FunctionExecutionCount = 0;
                }

                await PublishMetricsAsync(metrics);
            }
            finally
            {
                _intervalStopwatch = ValueStopwatch.StartNew();
            }
        }

        private async void OnFunctionMetricsPublishTimer(object state)
        {
            await OnPublishMetrics();
        }

        private async Task PublishMetricsAsync(Metrics metrics)
        {
            string fileName = string.Empty;

            try
            {
                bool metricsPublishEnabled = !string.IsNullOrEmpty(MetricsFilePath);
                if (metricsPublishEnabled && !PrepareDirectoryForFile())
                {
                    return;
                }

                string metricsContent = JsonConvert.SerializeObject(metrics);
                _logger.PublishingMetrics(metricsContent);

                if (metricsPublishEnabled)
                {
                    fileName = $"{Guid.NewGuid().ToString().ToLower()}.json";
                    string filePath = Path.Combine(MetricsFilePath, fileName);

                    using (var streamWriter = _fileSystem.File.CreateText(filePath))
                    {
                        await streamWriter.WriteAsync(metricsContent);
                    }
                }
            }
            catch (Exception ex) when (!ex.IsFatal())
            {
                // TODO: consider using a retry strategy here
                _logger.LogError(ex, $"Error writing metrics file '{fileName}'.");
            }
        }

        private bool PrepareDirectoryForFile()
        {
            if (string.IsNullOrEmpty(MetricsFilePath))
            {
                return false;
            }

            // ensure the directory exists
            _fileSystem.Directory.CreateDirectory(MetricsFilePath);

            var metricsDirectoryInfo = _fileSystem.DirectoryInfo.FromDirectoryName(MetricsFilePath);
            var files = metricsDirectoryInfo.GetFiles().OrderBy(p => p.CreationTime).ToList();

            // ensure we're under the max file count
            if (files.Count < _options.MaxFileCount)
            {
                return true;
            }

            // we're at or over limit
            // delete enough files that we have space to write a new one
            int numToDelete = files.Count - _options.MaxFileCount + 1;
            var filesToDelete = files.Take(numToDelete).ToArray();

            _logger.LogDebug($"Deleting {filesToDelete.Length} metrics file(s).");

            foreach (var file in filesToDelete)
            {
                try
                {
                    file.Delete();
                }
                catch (Exception ex) when (!ex.IsFatal())
                {
                    // best effort
                    _logger.LogError(ex, $"Error deleting metrics file '{file.FullName}'.");
                }
            }

            files = metricsDirectoryInfo.GetFiles().OrderBy(p => p.CreationTime).ToList();

            // return true if we have space for a new file
            return files.Count < _options.MaxFileCount;
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
            if (!_started)
            {
                return;
            }

            lock (_lock)
            {
                if (ActiveFunctionCount == 0)
                {
                    // we're transitioning from inactive to active
                    _instanceActivityStopwatch = ValueStopwatch.StartNew();
                }

                ActiveFunctionCount++;
            }
        }

        public void OnFunctionCompleted(string functionName, string invocationId)
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

                if (ActiveFunctionCount == 0)
                {
                    // we're transitioning from active to inactive accumulate the elapsed time,
                    // applying the minimum interval
                    var elapsedMS = _instanceActivityStopwatch.GetElapsedTime().TotalMilliseconds;
                    var duration = Math.Max(elapsedMS, _options.MinimumActivityIntervalMS);
                    FunctionExecutionTimeMS += (long)duration;
                }

                // for every completed invocation, increment our invocation count
                FunctionExecutionCount++;
            }
        }

        public void AddFunctionExecutionActivity(string functionName, string invocationId, int concurrency, string executionStage, bool success, long executionTimeSpan, string executionId, DateTime eventTimeStamp, DateTime functionStartTime)
        {
            // nothing to do here - we only care about Started/Completed events.
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
        }
    }
}
