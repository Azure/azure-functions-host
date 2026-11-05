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

namespace Microsoft.Azure.WebJobs.Script.WebHost.Metrics;

/// <summary>
/// Provides shared lifecycle, publication scheduling, and interval rounding for Flex billing publishers.
/// </summary>
public abstract partial class FlexConsumptionMetricsPublisherBase : IMetricsPublisher, IDisposable
{
    private readonly IOptionsMonitor<StandbyOptions> _standbyOptions;
    private readonly IEnvironment _environment;
    private IDisposable _standbyOptionsOnChangeSubscription;
    private Lifecycle _lifecycle;

    /// <summary>
    /// Initializes shared publisher dependencies. Derived publishers start scheduling after initializing their state.
    /// </summary>
    private protected FlexConsumptionMetricsPublisherBase(IEnvironment environment, IOptionsMonitor<StandbyOptions> standbyOptions,
        IOptions<FlexConsumptionMetricsPublisherOptions> options, ILogger logger, IFileSystem fileSystem,
        IHostMetricsProvider metricsProvider, TimeProvider timeProvider)
    {
        _standbyOptions = standbyOptions ?? throw new ArgumentNullException(nameof(standbyOptions));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        PublisherOptions = options?.Value ?? throw new ArgumentNullException(nameof(options));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        MetricsProvider = metricsProvider ?? throw new ArgumentNullException(nameof(metricsProvider));
        Clock = timeProvider ?? TimeProvider.System;
        FileManager = new LegionMetricsFileManager(PublisherOptions.MetricsFilePath, fileSystem ?? new FileSystem(), Logger, PublisherOptions.MaxFileCount);
    }

    private protected FlexConsumptionMetricsPublisherOptions PublisherOptions { get; }

    private protected ILogger Logger { get; }

    private protected IHostMetricsProvider MetricsProvider { get; }

    private protected object SyncLock { get; } = new();

    private protected LegionMetricsFileManager FileManager { get; }

    private protected TimeProvider Clock { get; }

    internal bool IsAlwaysReady { get; set; }

    internal LegionMetricsFileManager MetricsFileManager => FileManager;

    private protected bool IsStarted => _lifecycle is not null;

    private protected void Initialize()
    {
        if (_standbyOptions.CurrentValue.InStandbyMode)
        {
            Logger.LogDebug("Registering StandbyOptions change subscription.");
            _standbyOptionsOnChangeSubscription = _standbyOptions.OnChange(o =>
            {
                if (!o.InStandbyMode)
                {
                    Start();
                }
            });
        }
        else
        {
            Start();
        }
    }

    /// <summary>
    /// Starts publication when the host is specialized.
    /// </summary>
    public void Start()
    {
        lock (SyncLock)
        {
            if (IsStarted)
            {
                return;
            }

            IsAlwaysReady = string.Equals(_environment.GetEnvironmentVariable(EnvironmentSettingNames.FunctionsAlwaysReadyInstance),
                "1", StringComparison.Ordinal);
            OnStarted(Clock.GetUtcNow().UtcDateTime);
            Logger.LogInformation(
                $"Starting metrics publisher (AlwaysReady={IsAlwaysReady}, MetricsPath='{FileManager.MetricsFilePath}').");
            _lifecycle = new(this, TimeSpan.FromMilliseconds(PublisherOptions.InitialPublishDelayMS),
                TimeSpan.FromMilliseconds(PublisherOptions.MetricsPublishIntervalMS));
        }
    }

    private protected virtual void OnStarted(DateTime now)
    {
    }

    /// <inheritdoc />
    public virtual void Dispose()
    {
        Interlocked.Exchange(ref _lifecycle, null)?.Dispose();
        Interlocked.Exchange(ref _standbyOptionsOnChangeSubscription, null)?.Dispose();
    }

    internal async Task OnPublishMetrics(DateTime now, ValueStopwatch stopwatch)
    {
        try
        {
            await PublishMetricsAsync(now, stopwatch);
        }
        catch (Exception ex) when (!ex.IsFatal())
        {
            Logger.LogError(ex, "Error publishing metrics.");
        }
    }

    private protected abstract Task PublishMetricsAsync(DateTime now, ValueStopwatch stopwatch);

    /// <inheritdoc />
    public void OnFunctionStarted(string functionName, string invocationId)
        => OnFunctionStarted(functionName, invocationId, Clock.GetUtcNow().UtcDateTime);

    internal abstract void OnFunctionStarted(string functionName, string invocationId, DateTime now);

    /// <inheritdoc />
    public void OnFunctionCompleted(string functionName, string invocationId)
        => OnFunctionCompleted(functionName, invocationId, Clock.GetUtcNow().UtcDateTime);

    /// <inheritdoc />
    public void OnFunctionCompleted(string functionName, string invocationId, bool success)
        => OnFunctionCompleted(functionName, invocationId, Clock.GetUtcNow().UtcDateTime, success);

    internal abstract void OnFunctionCompleted(string functionName, string invocationId, DateTime now, bool success = false);

    /// <inheritdoc />
    public void AddFunctionExecutionActivity(string functionName, string invocationId, int concurrency, string executionStage,
        bool success, long executionTimeSpan, string executionId, DateTime eventTimeStamp, DateTime functionStartTime)
    {
        // Flex billing is accumulated from invocation lifecycle signals instead of execution logs.
    }

    private protected (long DurationMS, DateTime HighWatermark) MeasureActivityInterval(DateTime intervalStart, DateTime highWatermark, DateTime now)
    {
        DateTime start = highWatermark > intervalStart ? highWatermark : intervalStart;
        double elapsedMS = (now - start).TotalMilliseconds;
        if (elapsedMS <= 0)
        {
            return (0, highWatermark);
        }

        double duration = Math.Ceiling(Math.Max(elapsedMS, PublisherOptions.MinimumActivityIntervalMS) / PublisherOptions.MetricsGranularityMS)
            * PublisherOptions.MetricsGranularityMS;

        return ((long)duration, start.AddMilliseconds(duration));
    }

    internal class Metrics
    {
        /// <summary>
        /// Gets or sets the total time for the metrics interval.
        /// </summary>
        public long TotalTimeMS { get; set; }

        /// <summary>
        /// Gets or sets the total time duration that the instance had function activity during the interval.
        /// </summary>
        public long ExecutionTimeMS { get; set; }

        /// <summary>
        /// Gets or sets the total number of function invocations that completed during the interval.
        /// </summary>
        public long ExecutionCount { get; set; }

        /// <summary>
        /// Gets or sets the total number of function invocations that completed successfully during the interval.
        /// </summary>
        public long ExecutionSuccessCount { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the instance is AlwaysReady.
        /// </summary>
        public bool IsAlwaysReady { get; set; }

        /// <summary>
        /// Gets or sets the instance Id.
        /// </summary>
        public string InstanceId { get; set; }

        /// <summary>
        /// Gets or sets the function group name: http, durable, or a function name.
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
