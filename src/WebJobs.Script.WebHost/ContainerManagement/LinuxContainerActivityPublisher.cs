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
    public sealed class LinuxContainerActivityPublisher : IHostedService, IAsyncDisposable, ILinuxContainerActivityPublisher
    {
        public const string SpecializationCompleteEvent = "SpecializationCompleted";
        private const int InitialFlushIntervalMs = 5 * 1000; // 5 seconds
        private const int FlushIntervalMs = 20 * 1000; // 20 seconds
        private const int LockTimeOutMs = 1 * 1000; // 1 second

        private readonly ReaderWriterLockSlim _activitiesLock = new ReaderWriterLockSlim();
        private readonly IMeshServiceClient _meshServiceClient;
        private readonly ILogger<LinuxContainerActivityPublisher> _logger;
        private readonly int _initialFlushIntervalMs;
        private readonly int _flushIntervalMs;
        private readonly IOptionsMonitor<StandbyOptions> _standbyOptions;
        private readonly HashSet<ContainerFunctionExecutionActivity> _uniqueActivities;
        private IDisposable _standbyOptionsOnChangeSubscription;
        private DateTime _lastHeartBeatTime = DateTime.MinValue;
        private int _flushInProgress;
        private bool _initialPublish;
        private CancellationTokenSource _publishingCts;
        private Task _publishingTask;

        public LinuxContainerActivityPublisher(IOptionsMonitor<StandbyOptions> standbyOptions,
            IMeshServiceClient meshServiceClient, IEnvironment environment,
            ILogger<LinuxContainerActivityPublisher> logger, int flushIntervalMs = FlushIntervalMs, int initialFlushIntervalMs = InitialFlushIntervalMs)
        {
            if (!environment.IsAnyLinuxConsumption())
            {
                throw new NotSupportedException(
                    $"{nameof(LinuxContainerActivityPublisher)} is available in Linux consumption environment only");
            }

            _standbyOptions = standbyOptions ?? throw new ArgumentNullException(nameof(standbyOptions));
            _meshServiceClient = meshServiceClient;
            _logger = logger;
            _flushIntervalMs = flushIntervalMs;
            _initialFlushIntervalMs = initialFlushIntervalMs;
            _uniqueActivities = new HashSet<ContainerFunctionExecutionActivity>();
            _flushInProgress = 0;
            _initialPublish = true;
        }

        private void Start()
        {
            _logger.LogInformation($"Starting {nameof(LinuxContainerActivityPublisher)}");

            _publishingTask = StartPublishingAsync();
        }

        private async Task StartPublishingAsync()
        {
            _publishingCts = new CancellationTokenSource();

            try
            {
                while (!_publishingCts.IsCancellationRequested)
                {
                    try
                    {
                        var nextFlushDelay = _flushIntervalMs;

                        if (_initialPublish)
                        {
                            _initialPublish = false;
                            await PublishSpecializationCompleteEvent();

                            nextFlushDelay -= _initialFlushIntervalMs;
                        }
                        else
                        {
                            await FlushFunctionExecutionActivities();
                        }

                        await Task.Delay(nextFlushDelay, _publishingCts.Token);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,$"Error in {nameof(LinuxContainerActivityPublisher)} publishing loop.");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected in normal termination flow
            }

            _logger.LogInformation($"{nameof(LinuxContainerActivityPublisher)} publishing loop completed.");
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

            _publishingCts.Cancel();

            return Task.CompletedTask;
        }

        private async Task PublishSpecializationCompleteEvent()
        {
            try
            {
                await _meshServiceClient.NotifyHealthEvent(ContainerHealthEventType.Informational, GetType(),
                    SpecializationCompleteEvent);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"{nameof(PublishSpecializationCompleteEvent)} failed with {ex}",
                    nameof(PublishSpecializationCompleteEvent), ex);
            }
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
                _logger.LogError(exc, nameof(FlushFunctionExecutionActivities));
            }
        }

        private bool PublishActivity(ContainerFunctionExecutionActivity activity)
        {
            if (!_activitiesLock.TryEnterWriteLock(LockTimeOutMs))
            {
                return false;
            }

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

        private bool TryGetCurrentActivities(IList<ContainerFunctionExecutionActivity> currentActivities)
        {
            if (!_activitiesLock.TryEnterWriteLock(LockTimeOutMs))
            {
                return false;
            }

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

        public void PublishFunctionExecutionActivity(ContainerFunctionExecutionActivity activity)
        {
            if (_standbyOptions.CurrentValue.InStandbyMode)
            {
                return;
            }

            if (!PublishActivity(activity))
            {
                _logger.LogWarning($"Failed to add activity {activity}");
            }
        }

        public async ValueTask DisposeAsync()
        {
            // Wait for the publishing task to complete
            if (_publishingTask != null)
            {
                await _publishingTask;
            }

            _activitiesLock?.Dispose();
            _standbyOptionsOnChangeSubscription?.Dispose();
            _publishingCts?.Dispose();
        }
    }
}
