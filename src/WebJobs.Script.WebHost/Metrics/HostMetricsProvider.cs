// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Script.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Metrics
{
    public class HostMetricsProvider : IHostMetricsProvider, IDisposable
    {
        private readonly MeterListener _meterListener;
        private readonly IServiceProvider _serviceProvider;
        private readonly IOptionsMonitor<StandbyOptions> _standbyOptions;
        private readonly ILogger<HostMetricsProvider> _logger;
        private readonly IEnvironment _environment;

        private ConcurrentDictionary<string, long> _metricsCache = new();
        private IDisposable _standbyOptionsOnChangeSubscription;
        private bool _started = false;

        public HostMetricsProvider(IServiceProvider serviceProvider, IOptionsMonitor<StandbyOptions> standbyOptions,
            ILogger<HostMetricsProvider> logger, IEnvironment environment)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _standbyOptions = standbyOptions ?? throw new ArgumentNullException(nameof(standbyOptions));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _meterListener = new();

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

        public string FunctionGroup { get; private set; } = string.Empty;

        public string InstanceId { get; private set; } = string.Empty;

        public void Start()
        {
            _meterListener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name is HostMetrics.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);

                    var instanceIdTag = instrument.Meter.Tags.FirstOrDefault(t => t.Key == TelemetryAttributes.ServiceInstanceId);
                    InstanceId = instanceIdTag.Value?.ToString() ?? string.Empty;
                }
            };

            FunctionGroup = _environment.GetEnvironmentVariableOrDefault(EnvironmentSettingNames.FunctionsTargetGroup, string.Empty);

            if (string.IsNullOrEmpty(FunctionGroup))
            {
                _logger.LogDebug("{funcGroup} is null or empty after specialization.", EnvironmentSettingNames.FunctionsTargetGroup);
            }

            _logger.LogInformation("Starting host metrics provider.");

            _meterListener.SetMeasurementEventCallback<long>(OnMeasurementRecordedLong);
            _meterListener.Start();

            _started = true;
        }

        private void OnStandbyOptionsChange()
        {
            if (!_standbyOptions.CurrentValue.InStandbyMode)
            {
                Start();
            }
        }

        private void OnMeasurementRecordedLong(
            Instrument instrument,
            long measurement,
            ReadOnlySpan<KeyValuePair<string, object>> tags,
            object state)
        {
            if (!_started)
            {
                return;
            }

            if (instrument == null)
            {
                throw new ArgumentNullException(nameof(instrument));
            }

            AddOrUpdateMetricsCache(instrument.Name, measurement);
        }

        private void AddOrUpdateMetricsCache(string key, long value)
        {
            _metricsCache.AddOrUpdate(key,
                (k) => Math.Max(value, 0),
                (k, oldValue) => Math.Max(oldValue + value, 0));
        }

        public IReadOnlyDictionary<string, long> GetHostMetricsOrNull()
        {
            if (!HasMetrics())
            {
                return null;
            }

            var functionActivityStatusProvider = _serviceProvider.GetScriptHostServiceOrNull<IFunctionActivityStatusProvider>();
            if (functionActivityStatusProvider is not null)
            {
                var functionActivityStatus = functionActivityStatusProvider.GetStatus();
                AddOrUpdateMetricsCache(HostMetrics.ActiveInvocationCount, functionActivityStatus.OutstandingInvocations);
            }

            var metrics = Interlocked.Exchange(ref _metricsCache, new ConcurrentDictionary<string, long>());

            return metrics;
        }

        public bool HasMetrics() => !_metricsCache.IsEmpty;

        public void Dispose()
        {
            _meterListener?.Dispose();

            _standbyOptionsOnChangeSubscription?.Dispose();
            _standbyOptionsOnChangeSubscription = null;
        }
    }
}
