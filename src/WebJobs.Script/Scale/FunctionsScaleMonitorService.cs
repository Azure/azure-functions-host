// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Scale;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.Scale
{
    /// <summary>
    /// Service responsible for taking periodic scale metrics samples and persisting them.
    /// </summary>
    public class FunctionsScaleMonitorService : IHostedService, IDisposable
    {
        private readonly IPrimaryHostStateProvider _primaryHostStateProvider;
        private readonly IScaleMonitorManager _monitorManager;
        private readonly IScaleMetricsRepository _metricsRepository;
        private readonly IEnvironment _environment;
        private readonly ILogger _logger;
        private readonly Timer _timer;
        private readonly TimeSpan _interval;
        private readonly ScaleOptions _scaleOptions;
        private bool _disposed;

        public FunctionsScaleMonitorService(IScaleMonitorManager monitorManager, IScaleMetricsRepository metricsRepository, IPrimaryHostStateProvider primaryHostStateProvider, IEnvironment environment, ILoggerFactory loggerFactory, IOptions<ScaleOptions> scaleOptions)
        {
            _monitorManager = monitorManager;
            _metricsRepository = metricsRepository;
            _primaryHostStateProvider = primaryHostStateProvider;
            _environment = environment;
            _logger = loggerFactory.CreateLogger<FunctionsScaleMonitorService>();
            _scaleOptions = scaleOptions.Value;

            _interval = _scaleOptions.ScaleMetricsSampleInterval;
            _timer = new Timer(OnTimer, null, Timeout.Infinite, Timeout.Infinite);
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (_environment.IsRuntimeScaleMonitoringEnabled())
            {
                _logger.LogInformation("Runtime scale monitoring is enabled.");

                // start the timer by setting the due time
                SetTimerInterval((int)_interval.TotalMilliseconds);
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            // stop the timer if it has been started
            _timer.Change(Timeout.Infinite, Timeout.Infinite);

            return Task.CompletedTask;
        }

        private async void OnTimer(object state)
        {
            if (_primaryHostStateProvider.IsPrimary)
            {
                await TakeMetricsSamplesAsync();
            }

            SetTimerInterval((int)_interval.TotalMilliseconds);
        }

        private async Task TakeMetricsSamplesAsync()
        {
            try
            {
                // get the monitors
                // if the host is offline, no monitors will be returned
                var monitors = _monitorManager.GetMonitors();

                if (monitors.Any())
                {
                    _logger.LogDebug($"Taking metrics samples for {monitors.Count()} monitor(s).");

                    var metricsMap = new Dictionary<IScaleMonitor, ScaleMetrics>();
                    foreach (var monitor in monitors)
                    {
                        ScaleMetrics metrics = null;
                        try
                        {
                            // take a metrics sample for each monitor
                            metrics = await monitor.GetMetricsAsync();
                            metricsMap[monitor] = metrics;

                            // log the metrics json to provide visibility into monitor activity
                            var json = JsonConvert.SerializeObject(metrics);
                            _logger.LogDebug($"Scale metrics sample for monitor '{monitor.Descriptor.Id}': {json}");
                        }
                        catch (Exception exc) when (!exc.IsFatal())
                        {
                            // if a particular monitor fails, log and continue
                            _logger.LogError(exc, $"Failed to collect scale metrics sample for monitor '{monitor.Descriptor.Id}'.");
                        }
                    }

                    if (metricsMap.Count > 0)
                    {
                        // persist the metrics samples
                        await _metricsRepository.WriteMetricsAsync(metricsMap);
                    }
                }
            }
            catch (Exception exc) when (!exc.IsFatal())
            {
                _logger.LogError(exc, "Failed to collect/persist scale metrics.");
            }
        }

        private void SetTimerInterval(int dueTime)
        {
            if (!_disposed)
            {
                var timer = _timer;
                if (timer != null)
                {
                    try
                    {
                        _timer.Change(dueTime, Timeout.Infinite);
                    }
                    catch (ObjectDisposedException)
                    {
                        // might race with dispose
                    }
                }
            }
        }

        private void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _timer.Dispose();
                }

                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }
}
