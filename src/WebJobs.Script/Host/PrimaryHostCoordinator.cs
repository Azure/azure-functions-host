// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Script
{
    // This is used to determine which host instance is the "Primary"
    internal sealed class PrimaryHostCoordinator : IDisposable
    {
        internal const string LockBlobName = "host";
        internal const string HostContainerName = "azure-webjobs-hosts";

        private readonly Timer _timer;
        private readonly TimeSpan _leaseTimeout;
        private readonly TimeSpan _renewalInterval;
        private readonly TimeSpan _leaseRetryInterval;
        private readonly TraceWriter _traceWriter;
        private readonly ILogger _logger;
        private readonly string _hostId;
        private readonly string _instanceId;
        private IDistributedLock _lockHandle; // If non-null, then we own the lock.

        private bool _disposed;
        private bool _processingLease;
        private DateTime _lastRenewal;
        private TimeSpan _lastRenewalLatency;

        private IDistributedLockManager _lockManager;

        internal PrimaryHostCoordinator(IDistributedLockManager lockManager, TimeSpan leaseTimeout, string hostId, string instanceId, TraceWriter traceWriter,
            ILoggerFactory loggerFactory, TimeSpan? renewalInterval = null)
        {
            _leaseTimeout = leaseTimeout;
            _traceWriter = traceWriter;
            _hostId = hostId;
            _instanceId = instanceId;

            _lockManager = lockManager;
            if (lockManager == null)
            {
                throw new ArgumentNullException(nameof(lockManager));
            }

            // Renew the lease three seconds before it expires
            _renewalInterval = renewalInterval ?? leaseTimeout.Add(TimeSpan.FromSeconds(-3));

            // Attempt to acquire a lease every 5 seconds
            _leaseRetryInterval = TimeSpan.FromSeconds(5);

            _timer = new Timer(ProcessLeaseTimerTick, null, TimeSpan.Zero, _leaseRetryInterval);

            _logger = loggerFactory?.CreateLogger(ScriptConstants.LogCategoryHostGeneral);
        }

        public event EventHandler HasLeaseChanged;

        public bool HasLease => _lockHandle != null;

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

                if (previous != _lockHandle)
                {
                    OnHasLeaseChanged();
                }
            }
        }

        private void OnHasLeaseChanged() => HasLeaseChanged?.Invoke(this, EventArgs.Empty);

        public static PrimaryHostCoordinator Create(
            IDistributedLockManager lockManager,
            TimeSpan leaseTimeout,
            string hostId,
            string instanceId,
            TraceWriter traceWriter,
            ILoggerFactory loggerFactory,
            TimeSpan? renewalInterval = null)
        {
            if (leaseTimeout.TotalSeconds < 15 || leaseTimeout.TotalSeconds > 60)
            {
                throw new ArgumentOutOfRangeException(nameof(leaseTimeout), $"The {nameof(leaseTimeout)} should be between 15 and 60 seconds");
            }

            var manager = new PrimaryHostCoordinator(lockManager, leaseTimeout, hostId, instanceId, traceWriter, loggerFactory, renewalInterval);
            return manager;
        }

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
                            ProcessLeaseError(e.Message);
                            return true;
                        });
                    }

                    _processingLease = false;
                }, TaskContinuationOptions.ExecuteSynchronously);
        }

        private async Task AcquireOrRenewLeaseAsync()
        {
            if (!_disposed)
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
                    LockHandle = await _lockManager.TryLockAsync(null, lockName, _instanceId, null, _leaseTimeout, CancellationToken.None);
                    if (LockHandle == null)
                    {
                        // We didn't have the lease and failed to acquire it. Common if somebody else already has it.
                        // This is normal and does not warrant any logging.
                        return;
                    }

                    _lastRenewal = DateTime.UtcNow;
                    _lastRenewalLatency = _lastRenewal - requestStart;

                    string message = $"Host lock lease acquired by instance ID '{_instanceId}'.";
                    _traceWriter.Info(message);
                    _logger?.LogInformation(message);

                    // We've successfully acquired the lease, change the timer to use our renewal interval
                    SetTimerInterval(_renewalInterval);
                }
            }
        }

        internal static string GetBlobName(string hostId) => $"locks/{hostId}/{LockBlobName}";

        private void ProcessLeaseError(string reason)
        {
            if (HasLease)
            {
                ResetLease();

                string message = $"Failed to renew host lock lease: {reason}";
                _traceWriter.Info(message);
                _logger?.LogInformation(message);
            }
            else
            {
                string message = $"Host instance '{_instanceId}' failed to acquire host lock lease: {reason}";
                _traceWriter.Verbose(message);
                _logger?.LogDebug(message);
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

                    string message = $"Host instance '{_instanceId}' released lock lease.";
                    _traceWriter.Verbose(message);
                    _logger?.LogDebug(message);
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
    }
}
