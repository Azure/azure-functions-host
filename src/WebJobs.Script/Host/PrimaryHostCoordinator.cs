﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script
{
    // This is used to determine which host instance is the "Primary"
    internal sealed class PrimaryHostCoordinator : IHostedService, IDisposable
    {
        internal const string LockBlobName = "host";
        internal const string HostContainerName = "azure-webjobs-hosts";

        private readonly Timer _timer;
        private readonly TimeSpan _leaseTimeout;
        private readonly TimeSpan _renewalInterval;
        private readonly TimeSpan _leaseRetryInterval;
        private readonly ILogger _logger;
        private readonly IPrimaryHostStateProvider _primaryHostStateProvider;
        private readonly string _hostId;
        private readonly string _websiteInstanceId;
        private IDistributedLock _lockHandle; // If non-null, then we own the lock.

        private bool _disposed;
        private bool _processingLease;
        private DateTime _lastRenewal;
        private TimeSpan _lastRenewalLatency;

        private IDistributedLockManager _lockManager;

        public PrimaryHostCoordinator(IOptions<PrimaryHostCoordinatorOptions> coordinatorOptions, IOptions<JobHostOptions> jobHostOptions,
            IDistributedLockManager lockManager, ScriptSettingsManager settingsManager, IPrimaryHostStateProvider primaryHostStateProvider, ILoggerFactory loggerFactory)
        {
            _leaseTimeout = coordinatorOptions.Value.LeaseTimeout;
            _hostId = jobHostOptions.Value.HostId;
            _websiteInstanceId = settingsManager.AzureWebsiteInstanceId;

            _lockManager = lockManager;
            if (lockManager == null)
            {
                throw new ArgumentNullException(nameof(lockManager));
            }

            // Renew the lease three seconds before it expires
            _renewalInterval = coordinatorOptions.Value.RenewalInterval ?? _leaseTimeout.Add(TimeSpan.FromSeconds(-3));

            // Attempt to acquire a lease every 5 seconds
            _leaseRetryInterval = TimeSpan.FromSeconds(5);

            _timer = new Timer(ProcessLeaseTimerTick);

            _logger = loggerFactory.CreateLogger(ScriptConstants.LogCategoryHostGeneral);
            _primaryHostStateProvider = primaryHostStateProvider;
        }

        public event EventHandler HasLeaseChanged;

        private bool HasLease => _lockHandle != null;

        internal IDistributedLock LockHandle
        {
            get
            {
                return _lockHandle;
            }

            set
            {
                var previous = _lockHandle;
                _lockHandle = value;
                _primaryHostStateProvider.IsPrimary = HasLease;

                if (previous != _lockHandle)
                {
                    OnHasLeaseChanged();
                }
            }
        }

        private void OnHasLeaseChanged() => HasLeaseChanged?.Invoke(this, EventArgs.Empty);

        private void ProcessLeaseTimerTick(object state)
        {
            if (_processingLease)
            {
                return;
            }

            _processingLease = true;

            AcquireOrRenewLeaseAsync()
                .ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        t.Exception.Handle(e =>
                        {
                            ProcessLeaseError(Utility.FlattenException(e));
                            return true;
                        });
                    }

                    _processingLease = false;
                }, TaskContinuationOptions.ExecuteSynchronously);
        }

        private async Task AcquireOrRenewLeaseAsync()
        {
            string lockName = GetBlobName(_hostId);

            DateTime requestStart = DateTime.UtcNow;
            if (HasLease)
            {
                try
                {
                    await _lockManager.RenewAsync(LockHandle, CancellationToken.None);

                    _lastRenewal = DateTime.UtcNow;
                    _lastRenewalLatency = _lastRenewal - requestStart;
                }
                catch
                {
                    // The lease was 'stolen'. Log details for debugging.
                    string lastRenewalFormatted = _lastRenewal.ToString("yyyy-MM-ddTHH:mm:ss.FFFZ", CultureInfo.InvariantCulture);
                    int millisecondsSinceLastSuccess = (int)(DateTime.UtcNow - _lastRenewal).TotalMilliseconds;
                    int lastRenewalMilliseconds = (int)_lastRenewalLatency.TotalMilliseconds;
                    ProcessLeaseError($"Another host has acquired the lease. The last successful renewal completed at {lastRenewalFormatted} ({millisecondsSinceLastSuccess} milliseconds ago) with a duration of {lastRenewalMilliseconds} milliseconds.");
                }
            }
            else
            {
                string proposedLeaseId = _websiteInstanceId;
                LockHandle = await _lockManager.TryLockAsync(null, lockName, _websiteInstanceId, proposedLeaseId, _leaseTimeout, CancellationToken.None);
                if (LockHandle == null)
                {
                    // We didn't have the lease and failed to acquire it. Common if somebody else already has it.
                    // This is normal and does not warrant any logging.
                    return;
                }

                _lastRenewal = DateTime.UtcNow;
                _lastRenewalLatency = _lastRenewal - requestStart;

                string message = $"Host lock lease acquired by instance ID '{_websiteInstanceId}'.";
                _logger.LogInformation(message);

                // We've successfully acquired the lease, change the timer to use our renewal interval
                SetTimerInterval(_renewalInterval);
            }
        }

        internal static string GetBlobName(string hostId) => $"locks/{hostId}/{LockBlobName}";

        private void ProcessLeaseError(string reason)
        {
            if (HasLease)
            {
                ResetLease();

                string message = $"Failed to renew host lock lease: {reason}";
                _logger.LogInformation(message);
            }
            else
            {
                string message = $"Host instance '{_websiteInstanceId}' failed to acquire host lock lease: {reason}";
                _logger.LogDebug(message);
            }
        }

        private void ResetLease()
        {
            LockHandle = null;
            SetTimerInterval(_leaseRetryInterval);
        }

        private void SetTimerInterval(TimeSpan interval, TimeSpan? dueTimeout = null)
        {
            if (!_disposed)
            {
                _timer.Change(dueTimeout ?? interval, interval);
            }
        }

        private void TryReleaseLeaseIfOwned()
        {
            try
            {
                if (HasLease)
                {
                    Task.Run(() => _lockManager.ReleaseLockAsync(_lockHandle, CancellationToken.None)).GetAwaiter().GetResult();

                    string message = $"Host instance '{_websiteInstanceId}' released lock lease.";
                    _logger.LogDebug(message);
                }
            }
            catch (Exception)
            {
                // Best effort, the lease will expire if we fail to release it.
            }
        }

        private void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _timer.Dispose();

                    TryReleaseLeaseIfOwned();
                }

                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _timer.Change(TimeSpan.Zero, _leaseRetryInterval);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            return Task.CompletedTask;
        }
    }
}
