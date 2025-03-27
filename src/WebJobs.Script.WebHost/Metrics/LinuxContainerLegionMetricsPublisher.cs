// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO.Abstractions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Platform.Metrics.LinuxConsumption;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Metrics
{
    public sealed class LinuxContainerLegionMetricsPublisher : IMetricsPublisher, IDisposable
    {
        private readonly ILinuxConsumptionMetricsTracker _metricsTracker;
        private readonly LegionMetricsFileManager _metricsFileManager;
        private readonly TimeSpan _memorySnapshotInterval = TimeSpan.FromMilliseconds(1000);
        private readonly TimeSpan _timerStartDelay = TimeSpan.FromSeconds(2);
        private readonly IOptionsMonitor<StandbyOptions> _standbyOptions;
        private readonly IDisposable _standbyOptionsOnChangeSubscription;
        private readonly IEnvironment _environment;
        private readonly ILogger _logger;
        private readonly IScriptHostManager _scriptHostManager;
        private readonly string _containerName;

        private IMetricsLogger _metricsLogger;
        private TimeSpan _metricPublishInterval;
        private Process _process;
        private Timer _processMonitorTimer;
        private Timer _metricsPublisherTimer;
        private bool _initialized = false;

        public LinuxContainerLegionMetricsPublisher(IEnvironment environment, IOptionsMonitor<StandbyOptions> standbyOptions,
            IOptions<LinuxConsumptionLegionMetricsPublisherOptions> options, ILogger<LinuxContainerLegionMetricsPublisher> logger,
            IFileSystem fileSystem, ILinuxConsumptionMetricsTracker metricsTracker, IScriptHostManager scriptHostManager,
            int? metricsPublishIntervalMS = null)
        {
            _standbyOptions = standbyOptions ?? throw new ArgumentNullException(nameof(standbyOptions));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _metricsTracker = metricsTracker ?? throw new ArgumentNullException(nameof(metricsTracker));
            _scriptHostManager = scriptHostManager ?? throw new ArgumentNullException(nameof(scriptHostManager));
            _containerName = options.Value.ContainerName;

            // Set this to 15 minutes worth of files
            _metricPublishInterval = TimeSpan.FromMilliseconds(metricsPublishIntervalMS ?? options.Value.MetricsPublishIntervalMS);
            int maxFileCount = 15 * (int)Math.Ceiling(1.0 * 60 / _metricPublishInterval.TotalSeconds);

            _metricsFileManager = new LegionMetricsFileManager(options.Value.MetricsFilePath, fileSystem, logger, maxFileCount);

            _processMonitorTimer = new Timer(OnProcessMonitorTimer, null, Timeout.Infinite, Timeout.Infinite);
            _metricsPublisherTimer = new Timer(OnFunctionMetricsPublishTimer, null, Timeout.Infinite, Timeout.Infinite);

            _metricsTracker.OnDiagnosticEvent += OnMetricsDiagnosticEvent;

            if (_standbyOptions.CurrentValue.InStandbyMode)
            {
                _standbyOptionsOnChangeSubscription = _standbyOptions.OnChange(o => OnStandbyOptionsChange(o));
            }
            else
            {
                Start();
            }
        }

        public IMetricsLogger MetricsLogger
        {
            get
            {
                if (_metricsLogger == null)
                {
                    if (!Utility.TryGetHostService<IMetricsLogger>(_scriptHostManager, out _metricsLogger))
                    {
                        throw new InvalidOperationException($"Unable to resolve {nameof(IMetricsLogger)} service.");
                    }
                }

                return _metricsLogger;
            }
        }

        public void Initialize()
        {
            _process = Process.GetCurrentProcess();
            _initialized = true;
        }

        public void Start()
        {
            Initialize();

            // start the timers by setting the due time
            SetTimerInterval(_processMonitorTimer, _timerStartDelay);
            SetTimerInterval(_metricsPublisherTimer, _metricPublishInterval);

            _logger.LogInformation("Starting metrics publisher for container : {ContainerName}.", _containerName);
        }

        private void OnStandbyOptionsChange(StandbyOptions standbyOptions)
        {
            if (!standbyOptions.InStandbyMode)
            {
                Start();
            }
        }

        public void AddFunctionExecutionActivity(string functionName, string invocationId, int concurrency, string executionStage, bool success, long executionTimeSpan, string executionId, DateTime eventTimeStamp, DateTime functionStartTime)
        {
            if (!_initialized)
            {
                return;
            }

            Enum.TryParse(executionStage, out FunctionExecutionStage functionExecutionStage);

            FunctionActivity activity = new FunctionActivity
            {
                FunctionName = functionName,
                InvocationId = invocationId,
                Concurrency = concurrency,
                ExecutionStage = functionExecutionStage,
                ExecutionId = executionId,
                IsSucceeded = success,
                ExecutionTimeSpanInMs = executionTimeSpan,
                EventTimeStamp = eventTimeStamp,
                StartTime = functionStartTime
            };

            _metricsTracker.AddFunctionActivity(activity);
        }

        public void AddMemoryActivity(DateTime timeStampUtc, long data)
        {
            if (!_initialized)
            {
                return;
            }

            var memoryActivity = new MemoryActivity
            {
                CommitSizeInBytes = data,
                EventTimeStamp = timeStampUtc
            };

            _metricsTracker.AddMemoryActivity(memoryActivity);
        }

        private async void OnFunctionMetricsPublishTimer(object state)
        {
            await OnPublishMetricsAsync();
        }

        internal async Task OnPublishMetricsAsync()
        {
            try
            {
                if (_metricsTracker.TryGetMetrics(out LinuxConsumptionMetrics trackedMetrics))
                {
                    var metricsToPublish = new Metrics
                    {
                        FunctionActivity = trackedMetrics.FunctionActivity,
                        ExecutionCount = trackedMetrics.FunctionExecutionCount,
                        ExecutionTimeMS = trackedMetrics.FunctionExecutionTimeMS
                    };

                    await _metricsFileManager.PublishMetricsAsync(metricsToPublish);
                }
            }
            catch (Exception ex) when (!ex.IsFatal())
            {
                // ensure no background exceptions escape
                _logger.LogError(ex, $"Error publishing metrics.");
            }
            finally
            {
                SetTimerInterval(_metricsPublisherTimer, _metricPublishInterval);
            }
        }

        private void OnProcessMonitorTimer(object state)
        {
            try
            {
                _process.Refresh();
                var commitSizeBytes = _process.WorkingSet64;
                if (commitSizeBytes != 0)
                {
                    AddMemoryActivity(DateTime.UtcNow, commitSizeBytes);
                }
            }
            catch (Exception e)
            {
                // throwing this exception will mask other underlying exceptions.
                // Log and let other interesting exceptions bubble up.
                _logger.LogError(e, nameof(OnProcessMonitorTimer));
            }
            finally
            {
                SetTimerInterval(_processMonitorTimer, _memorySnapshotInterval);
            }
        }

        private void SetTimerInterval(Timer timer, TimeSpan dueTime)
        {
            try
            {
                timer?.Change((int)dueTime.TotalMilliseconds, Timeout.Infinite);
            }
            catch (ObjectDisposedException)
            {
                // might race with dispose
            }
            catch (Exception e)
            {
                _logger.LogError(e, nameof(SetTimerInterval));
            }
        }

        private void OnMetricsDiagnosticEvent(object sender, DiagnosticEventArgs e)
        {
            MetricsLogger.LogEvent(e.EventName);
        }

        public void OnFunctionStarted(string functionName, string invocationId)
        {
            // nothing to do
        }

        public void OnFunctionCompleted(string functionName, string invocationId)
        {
            // nothing to do
        }

        public void Dispose()
        {
            _processMonitorTimer?.Dispose();
            _processMonitorTimer = null;

            _metricsPublisherTimer?.Dispose();
            _metricsPublisherTimer = null;

            _metricsTracker.OnDiagnosticEvent -= OnMetricsDiagnosticEvent;
        }

        internal class Metrics
        {
            /// <summary>
            /// Gets or sets a measure of the function activity for the interval.
            /// </summary>
            public long FunctionActivity { get; set; }

            /// <summary>
            /// Gets or sets the total execution duration for all functions during this interval.
            /// </summary>
            public long ExecutionTimeMS { get; set; }

            /// <summary>
            /// Gets or sets the total number of functions invocations that
            /// completed during the interval.
            /// </summary>
            public long ExecutionCount { get; set; }
        }
    }
}