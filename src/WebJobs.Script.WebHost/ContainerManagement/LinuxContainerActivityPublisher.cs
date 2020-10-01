// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.ContainerManagement
{
    public class LinuxContainerActivityPublisher : IHostedService, IDisposable, ILinuxContainerActivityPublisher
    {
        private const int FlushIntervalMs = 20 * 1000; // 20 seconds
        private const int LockTimeOutMs = 1 * 1000; // 1 second

        private readonly ReaderWriterLockSlim _activitiesLock = new ReaderWriterLockSlim();
        private readonly IMeshServiceClient _meshServiceClient;
        private readonly ILogger<LinuxContainerActivityPublisher> _logger;
        private readonly int _flushIntervalMs;
        private readonly IOptionsMonitor<StandbyOptions> _standbyOptions;
        private readonly HashSet<ContainerFunctionExecutionActivity> _uniqueActivities;
        private IDisposable _standbyOptionsOnChangeSubscription;
        private DateTime _lastHeartBeatTime = DateTime.MinValue;
        private Timer _timer;
        private int _flushInProgress;

        public LinuxContainerActivityPublisher(IOptionsMonitor<StandbyOptions> standbyOptions,
            IMeshServiceClient meshServiceClient, IEnvironment environment,
            ILogger<LinuxContainerActivityPublisher> logger, int flushIntervalMs = FlushIntervalMs)
        {
            if (!environment.IsLinuxConsumption())
            {
                throw new NotSupportedException(
                    $"{nameof(LinuxContainerActivityPublisher)} is available in Linux consumption environment only");
            }

            _standbyOptions = standbyOptions ?? throw new ArgumentNullException(nameof(standbyOptions));
            _meshServiceClient = meshServiceClient;
            _logger = logger;
            _flushIntervalMs = flushIntervalMs;
            _timer = new Timer(OnTimer, null, Timeout.Infinite, Timeout.Infinite);
            _uniqueActivities = new HashSet<ContainerFunctionExecutionActivity>();
            _flushInProgress = 0;
        }

        private void Start()
        {
            _logger.LogInformation($"Starting {nameof(LinuxContainerActivityPublisher)}");

            // start the timer by setting the due time
            SetTimerInterval(_flushIntervalMs);
        }

        private void OnStandbyOptionsChange()
        {
            _logger.LogInformation($"Triggering {nameof(OnStandbyOptionsChange)}");

            if (!_standbyOptions.CurrentValue.InStandbyMode)
            {
                Start();
            }
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation($"Initializing {nameof(LinuxContainerActivityPublisher)}");

            if (_standbyOptions.CurrentValue.InStandbyMode)
            {
                _logger.LogInformation($"Registering {nameof(_standbyOptionsOnChangeSubscription)}");
                _standbyOptionsOnChangeSubscription = _standbyOptions.OnChange(o => OnStandbyOptionsChange());
            }
            else
            {
                Start();
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation($"Stopping {nameof(LinuxContainerActivityPublisher)}");

            // stop the timer if it has been started
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);

            return Task.CompletedTask;
        }

        private async void OnTimer(object state)
        {
            await FlushFunctionExecutionActivities();
            SetTimerInterval(_flushIntervalMs);
        }

        private async Task FlushFunctionExecutionActivities()
        {
            try
            {
                if (Interlocked.CompareExchange(ref _flushInProgress, 1, 0) == 0)
                {
                    try
                    {
                        var currentActivities = new List<ContainerFunctionExecutionActivity>();
                        if (TryGetCurrentActivities(currentActivities))
                        {
                            if (_lastHeartBeatTime.AddMinutes(5) < DateTime.UtcNow)
                            {
                                _logger.LogDebug($"Current activities count = {currentActivities.Count}");
                                _lastHeartBeatTime = DateTime.UtcNow;
                            }

                            if (currentActivities.Any())
                            {
                                _logger.LogDebug($"Flushing {currentActivities.Count} function activities");
                                await _meshServiceClient.PublishContainerActivity(currentActivities);
                            }
                        }
                        else
                        {
                            _logger.LogWarning($"Failed to fetch {nameof(ContainerFunctionExecutionActivity)}");
                        }
                    }
                    finally
                    {
                        _flushInProgress = 0;
                    }
                }
            }
            catch (Exception exc) when (!exc.IsFatal())
            {
                _logger.LogError(exc, $"{nameof(FlushFunctionExecutionActivities)}");
            }
        }

        private void SetTimerInterval(int dueTime)
        {
            var timer = _timer;
            try
            {
                timer?.Change(dueTime, Timeout.Infinite);
            }
            catch (ObjectDisposedException)
            {
                // might race with dispose
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"{nameof(SetTimerInterval)}");
            }
        }

        public void Dispose()
        {
            if (_timer != null)
            {
                _timer?.Dispose();
                _timer = null;
            }
        }

        private bool PublishActivity(ContainerFunctionExecutionActivity activity)
        {
            if (_activitiesLock.TryEnterWriteLock(LockTimeOutMs))
            {
                try
                {
                    _uniqueActivities.Add(activity);
                }
                finally
                {
                    _activitiesLock.ExitWriteLock();
                }
                return true;
            }

            return false;
        }

        private bool TryGetCurrentActivities(IList<ContainerFunctionExecutionActivity> currentActivities)
        {
            if (_activitiesLock.TryEnterWriteLock(LockTimeOutMs))
            {
                try
                {
                    foreach (var activity in _uniqueActivities)
                    {
                        currentActivities.Add(activity);
                    }
                    _uniqueActivities.Clear();
                }
                finally
                {
                    _activitiesLock.ExitWriteLock();
                }
                return true;
            }

            return false;
        }

        public void PublishFunctionExecutionActivity(ContainerFunctionExecutionActivity activity)
        {
            if (!_standbyOptions.CurrentValue.InStandbyMode)
            {
                if (!PublishActivity(activity))
                {
                    _logger.LogWarning($"Failed to add activity {activity}");
                }
            }
        }
    }
}
