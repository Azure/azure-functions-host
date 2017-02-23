// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Bindings.Path;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Microsoft.Azure.WebJobs.Host.Timers;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host
{
    /// <summary>
    /// Encapsulates and manages blob leases for Singleton locks.
    /// </summary>
    internal class SingletonManager
    {
        internal const string FunctionInstanceMetadataKey = "FunctionInstance";
        private readonly INameResolver _nameResolver;
        private readonly IWebJobsExceptionHandler _exceptionHandler;
        private readonly SingletonConfiguration _config;
        private readonly IStorageAccountProvider _accountProvider;
        private ConcurrentDictionary<string, IStorageBlobDirectory> _lockDirectoryMap = new ConcurrentDictionary<string, IStorageBlobDirectory>(StringComparer.OrdinalIgnoreCase);
        private TimeSpan _minimumLeaseRenewalInterval = TimeSpan.FromSeconds(1);
        private TraceWriter _trace;
        private IHostIdProvider _hostIdProvider;
        private string _hostId;

        // For mock testing only
        internal SingletonManager()
        {
        }

        public SingletonManager(IStorageAccountProvider accountProvider, IWebJobsExceptionHandler exceptionHandler, SingletonConfiguration config, TraceWriter trace, IHostIdProvider hostIdProvider, INameResolver nameResolver = null)
        {
            _accountProvider = accountProvider;
            _nameResolver = nameResolver;
            _exceptionHandler = exceptionHandler;
            _config = config;
            _trace = trace;
            _hostIdProvider = hostIdProvider;
        }

        internal virtual SingletonConfiguration Config
        {
            get
            {
                return _config;
            }
        }

        internal string HostId
        {
            get
            {
                if (_hostId == null)
                {
                    _hostId = _hostIdProvider.GetHostIdAsync(CancellationToken.None).Result;
                }
                return _hostId;
            }
        }

        // for testing
        internal TimeSpan MinimumLeaseRenewalInterval
        {
            get
            {
                return _minimumLeaseRenewalInterval;
            }
            set
            {
                _minimumLeaseRenewalInterval = value;
            }
        }

        public async virtual Task<object> LockAsync(string lockId, string functionInstanceId, SingletonAttribute attribute, CancellationToken cancellationToken)
        {
            object lockHandle = await TryLockAsync(lockId, functionInstanceId, attribute, cancellationToken);

            if (lockHandle == null)
            {
                TimeSpan acquisitionTimeout = attribute.LockAcquisitionTimeout != null
                    ? TimeSpan.FromSeconds(attribute.LockAcquisitionTimeout.Value) :
                    _config.LockAcquisitionTimeout;
                throw new TimeoutException(string.Format("Unable to acquire singleton lock blob lease for blob '{0}' (timeout of {1} exceeded).", lockId, acquisitionTimeout.ToString("g")));
            }

            return lockHandle;
        }

        public async virtual Task<object> TryLockAsync(string lockId, string functionInstanceId, SingletonAttribute attribute, CancellationToken cancellationToken, bool retry = true)
        {
            IStorageBlobDirectory lockDirectory = GetLockDirectory(attribute.Account);
            IStorageBlockBlob lockBlob = lockDirectory.GetBlockBlobReference(lockId);
            TimeSpan lockPeriod = GetLockPeriod(attribute, _config);
            string leaseId = await TryAcquireLeaseAsync(lockBlob, lockPeriod, cancellationToken);
            if (string.IsNullOrEmpty(leaseId) && retry)
            {
                // Someone else has the lease. Continue trying to periodically get the lease for
                // a period of time
                TimeSpan acquisitionTimeout = attribute.LockAcquisitionTimeout != null
                    ? TimeSpan.FromSeconds(attribute.LockAcquisitionTimeout.Value) :
                    _config.LockAcquisitionTimeout;

                TimeSpan timeWaited = TimeSpan.Zero;
                while (string.IsNullOrEmpty(leaseId) && (timeWaited < acquisitionTimeout))
                {
                    await Task.Delay(_config.LockAcquisitionPollingInterval);
                    timeWaited += _config.LockAcquisitionPollingInterval;
                    leaseId = await TryAcquireLeaseAsync(lockBlob, lockPeriod, cancellationToken);
                }
            }

            if (string.IsNullOrEmpty(leaseId))
            {
                return null;
            }

            _trace.Verbose(string.Format(CultureInfo.InvariantCulture, "Singleton lock acquired ({0})", lockId), source: TraceSource.Execution);

            if (!string.IsNullOrEmpty(functionInstanceId))
            {
                await WriteLeaseBlobMetadata(lockBlob, leaseId, functionInstanceId, cancellationToken);
            }

            SingletonLockHandle lockHandle = new SingletonLockHandle
            {
                LeaseId = leaseId,
                LockId = lockId,
                Blob = lockBlob,
                LeaseRenewalTimer = CreateLeaseRenewalTimer(lockBlob, leaseId, lockId, lockPeriod, _exceptionHandler)
            };

            // start the renewal timer, which ensures that we maintain our lease until
            // the lock is released
            lockHandle.LeaseRenewalTimer.Start();

            return lockHandle;
        }

        public async virtual Task ReleaseLockAsync(object lockHandle, CancellationToken cancellationToken)
        {
            SingletonLockHandle singletonLockHandle = (SingletonLockHandle)lockHandle;

            if (singletonLockHandle.LeaseRenewalTimer != null)
            {
                await singletonLockHandle.LeaseRenewalTimer.StopAsync(cancellationToken);
            }

            await ReleaseLeaseAsync(singletonLockHandle.Blob, singletonLockHandle.LeaseId, cancellationToken);

            _trace.Verbose(string.Format(CultureInfo.InvariantCulture, "Singleton lock released ({0})", singletonLockHandle.LockId), source: TraceSource.Execution);
        }

        public string FormatLockId(MethodInfo method, SingletonScope scope, string scopeId)
        {
            return FormatLockId(method, scope, HostId, scopeId);
        }

        public static string FormatLockId(MethodInfo method, SingletonScope scope, string hostId, string scopeId)
        {
            if (string.IsNullOrEmpty(hostId))
            {
                throw new ArgumentNullException("hostId");
            }

            string lockId = string.Empty;
            if (scope == SingletonScope.Function)
            {
                lockId += string.Format(CultureInfo.InvariantCulture, "{0}.{1}", method.DeclaringType.FullName, method.Name);
            }

            if (!string.IsNullOrEmpty(scopeId))
            {
                if (!string.IsNullOrEmpty(lockId))
                {
                    lockId += ".";
                }
                lockId += scopeId;
            }

            lockId = string.Format(CultureInfo.InvariantCulture, "{0}/{1}", hostId, lockId);

            return lockId;
        }

        public string GetBoundScopeId(string scopeId, IReadOnlyDictionary<string, object> bindingData = null)
        {
            if (_nameResolver != null)
            {
                scopeId = _nameResolver.ResolveWholeString(scopeId);
            }

            if (bindingData != null)
            {
                BindingTemplate bindingTemplate = BindingTemplate.FromString(scopeId);
                IReadOnlyDictionary<string, string> parameters = BindingDataPathHelper.ConvertParameters(bindingData);
                return bindingTemplate.Bind(parameters);
            }
            else
            {
                return scopeId;
            }
        }

        public static SingletonAttribute GetFunctionSingletonOrNull(MethodInfo method, bool isTriggered)
        {
            if (!isTriggered &&
                method.GetCustomAttributes<SingletonAttribute>().Any(p => p.Mode == SingletonMode.Listener))
            {
                throw new NotSupportedException("SingletonAttribute using mode 'Listener' cannot be applied to non-triggered functions.");
            }

            SingletonAttribute[] singletonAttributes = method.GetCustomAttributes<SingletonAttribute>().Where(p => p.Mode == SingletonMode.Function).ToArray();
            SingletonAttribute singletonAttribute = null;
            if (singletonAttributes.Length > 1)
            {
                throw new NotSupportedException("Only one SingletonAttribute using mode 'Function' is allowed.");
            }
            else if (singletonAttributes.Length == 1)
            {
                singletonAttribute = singletonAttributes[0];
                ValidateSingletonAttribute(singletonAttribute, SingletonMode.Function);
            }

            return singletonAttribute;
        }

        /// <summary>
        /// Creates and returns singleton listener scoped to the host.
        /// </summary>
        /// <param name="innerListener">The inner listener to wrap.</param>
        /// <param name="scopeId">The scope ID to use.</param>
        /// <returns>The singleton listener.</returns>
        public SingletonListener CreateHostSingletonListener(IListener innerListener, string scopeId)
        {
            SingletonAttribute singletonAttribute = new SingletonAttribute(scopeId, SingletonScope.Host)
            {
                Mode = SingletonMode.Listener
            };
            return new SingletonListener(null, singletonAttribute, this, innerListener, _trace);
        }

        public static SingletonAttribute GetListenerSingletonOrNull(Type listenerType, MethodInfo method)
        {
            // First check the method, then the listener class. This allows a method to override an implicit
            // listener singleton.
            SingletonAttribute singletonAttribute = null;
            SingletonAttribute[] singletonAttributes = method.GetCustomAttributes<SingletonAttribute>().Where(p => p.Mode == SingletonMode.Listener).ToArray();
            if (singletonAttributes.Length > 1)
            {
                throw new NotSupportedException("Only one SingletonAttribute using mode 'Listener' is allowed.");
            }
            else if (singletonAttributes.Length == 1)
            {
                singletonAttribute = singletonAttributes[0];
            }
            else
            {
                singletonAttribute = listenerType.GetCustomAttributes<SingletonAttribute>().SingleOrDefault(p => p.Mode == SingletonMode.Listener);
            }

            if (singletonAttribute != null)
            {
                ValidateSingletonAttribute(singletonAttribute, SingletonMode.Listener);
            }

            return singletonAttribute;
        }

        internal static void ValidateSingletonAttribute(SingletonAttribute attribute, SingletonMode mode)
        {
            if (attribute.Scope == SingletonScope.Host && string.IsNullOrEmpty(attribute.ScopeId))
            {
                throw new InvalidOperationException("A ScopeId value must be provided when using scope 'Host'.");
            }

            if (mode == SingletonMode.Listener && attribute.Scope == SingletonScope.Host)
            {
                throw new InvalidOperationException("Scope 'Host' cannot be used when the mode is set to 'Listener'.");
            }
        }

        public async virtual Task<string> GetLockOwnerAsync(SingletonAttribute attribute, string lockId, CancellationToken cancellationToken)
        {
            IStorageBlobDirectory lockDirectory = GetLockDirectory(attribute.Account);
            IStorageBlockBlob lockBlob = lockDirectory.GetBlockBlobReference(lockId);

            await ReadLeaseBlobMetadata(lockBlob, cancellationToken);

            // if the lease is Available, then there is no current owner
            // (any existing owner value is the last owner that held the lease)
            if (lockBlob.Properties.LeaseState == LeaseState.Available &&
                lockBlob.Properties.LeaseStatus == LeaseStatus.Unlocked)
            {
                return null;
            }

            string owner = string.Empty;
            lockBlob.Metadata.TryGetValue(FunctionInstanceMetadataKey, out owner);

            return owner;
        }

        internal IStorageBlobDirectory GetLockDirectory(string accountName)
        {
            if (string.IsNullOrEmpty(accountName))
            {
                accountName = ConnectionStringNames.Storage;
            }

            IStorageBlobDirectory storageDirectory = null;
            if (!_lockDirectoryMap.TryGetValue(accountName, out storageDirectory))
            {
                Task<IStorageAccount> task = _accountProvider.GetAccountAsync(accountName, CancellationToken.None);
                IStorageAccount storageAccount = task.Result;
                // singleton requires block blobs, cannot be premium
                storageAccount.AssertTypeOneOf(StorageAccountType.GeneralPurpose, StorageAccountType.BlobOnly);
                IStorageBlobClient blobClient = storageAccount.CreateBlobClient();
                storageDirectory = blobClient.GetContainerReference(HostContainerNames.Hosts)
                                       .GetDirectoryReference(HostDirectoryNames.SingletonLocks);
                _lockDirectoryMap[accountName] = storageDirectory;
            }

            return storageDirectory;
        }

        internal static TimeSpan GetLockPeriod(SingletonAttribute attribute, SingletonConfiguration config)
        {
            return attribute.Mode == SingletonMode.Listener ?
                    config.ListenerLockPeriod : config.LockPeriod;
        }

        private ITaskSeriesTimer CreateLeaseRenewalTimer(IStorageBlockBlob leaseBlob, string leaseId, string lockId, TimeSpan leasePeriod,
            IWebJobsExceptionHandler exceptionHandler)
        {
            // renew the lease when it is halfway to expiring   
            TimeSpan normalUpdateInterval = new TimeSpan(leasePeriod.Ticks / 2);

            IDelayStrategy speedupStrategy = new LinearSpeedupStrategy(normalUpdateInterval, MinimumLeaseRenewalInterval);
            ITaskSeriesCommand command = new RenewLeaseCommand(leaseBlob, leaseId, lockId, speedupStrategy, _trace, leasePeriod);
            return new TaskSeriesTimer(command, exceptionHandler, Task.Delay(normalUpdateInterval));
        }

        private static async Task<string> TryAcquireLeaseAsync(IStorageBlockBlob blob, TimeSpan leasePeriod, CancellationToken cancellationToken)
        {
            bool blobDoesNotExist = false;
            try
            {
                // Optimistically try to acquire the lease. The blob may not yet
                // exist. If it doesn't we handle the 404, create it, and retry below
                return await blob.AcquireLeaseAsync(leasePeriod, null, cancellationToken);
            }
            catch (StorageException exception)
            {
                if (exception.RequestInformation != null)
                {
                    if (exception.RequestInformation.HttpStatusCode == 409)
                    {
                        return null;
                    }
                    else if (exception.RequestInformation.HttpStatusCode == 404)
                    {
                        blobDoesNotExist = true;
                    }
                    else
                    {
                        throw;
                    }
                }
                else
                {
                    throw;
                }
            }

            if (blobDoesNotExist)
            {
                await TryCreateAsync(blob, cancellationToken);

                try
                {
                    return await blob.AcquireLeaseAsync(leasePeriod, null, cancellationToken);
                }
                catch (StorageException exception)
                {
                    if (exception.RequestInformation != null &&
                        exception.RequestInformation.HttpStatusCode == 409)
                    {
                        return null;
                    }
                    else
                    {
                        throw;
                    }
                }
            }

            return null;
        }

        private static async Task ReleaseLeaseAsync(IStorageBlockBlob blob, string leaseId, CancellationToken cancellationToken)
        {
            try
            {
                // Note that this call returns without throwing if the lease is expired. See the table at:
                // http://msdn.microsoft.com/en-us/library/azure/ee691972.aspx
                await blob.ReleaseLeaseAsync(
                    accessCondition: new AccessCondition { LeaseId = leaseId },
                    options: null,
                    operationContext: null,
                    cancellationToken: cancellationToken);
            }
            catch (StorageException exception)
            {
                if (exception.RequestInformation != null)
                {
                    if (exception.RequestInformation.HttpStatusCode == 404 ||
                        exception.RequestInformation.HttpStatusCode == 409)
                    {
                        // if the blob no longer exists, or there is another lease
                        // now active, there is nothing for us to release so we can
                        // ignore
                    }
                    else
                    {
                        throw;
                    }
                }
                else
                {
                    throw;
                }
            }
        }

        private static async Task<bool> TryCreateAsync(IStorageBlockBlob blob, CancellationToken cancellationToken)
        {
            bool isContainerNotFoundException = false;

            try
            {
                await blob.UploadTextAsync(string.Empty, cancellationToken: cancellationToken);
                return true;
            }
            catch (StorageException exception)
            {
                if (exception.RequestInformation != null)
                {
                    if (exception.RequestInformation.HttpStatusCode == 404)
                    {
                        isContainerNotFoundException = true;
                    }
                    else if (exception.RequestInformation.HttpStatusCode == 409 ||
                             exception.RequestInformation.HttpStatusCode == 412)
                    {
                        // The blob already exists, or is leased by someone else
                        return false;
                    }
                    else
                    {
                        throw;
                    }
                }
                else
                {
                    throw;
                }
            }

            Debug.Assert(isContainerNotFoundException);
            await blob.Container.CreateIfNotExistsAsync(cancellationToken);

            try
            {
                await blob.UploadTextAsync(string.Empty, cancellationToken: cancellationToken);
                return true;
            }
            catch (StorageException exception)
            {
                if (exception.RequestInformation != null &&
                    (exception.RequestInformation.HttpStatusCode == 409 || exception.RequestInformation.HttpStatusCode == 412))
                {
                    // The blob already exists, or is leased by someone else
                    return false;
                }
                else
                {
                    throw;
                }
            }
        }

        private static async Task WriteLeaseBlobMetadata(IStorageBlockBlob blob, string leaseId, string functionInstanceId, CancellationToken cancellationToken)
        {
            blob.Metadata.Add(FunctionInstanceMetadataKey, functionInstanceId);

            await blob.SetMetadataAsync(
                accessCondition: new AccessCondition { LeaseId = leaseId },
                options: null,
                operationContext: null,
                cancellationToken: cancellationToken);
        }

        private static async Task ReadLeaseBlobMetadata(IStorageBlockBlob blob, CancellationToken cancellationToken)
        {
            try
            {
                await blob.FetchAttributesAsync(cancellationToken);
            }
            catch (StorageException exception)
            {
                if (exception.RequestInformation != null &&
                    exception.RequestInformation.HttpStatusCode == 404)
                {
                    // the blob no longer exists
                }
                else
                {
                    throw;
                }
            }
        }

        internal class SingletonLockHandle
        {
            public string LeaseId { get; set; }
            public string LockId { get; set; }
            public IStorageBlockBlob Blob { get; set; }
            public ITaskSeriesTimer LeaseRenewalTimer { get; set; }
        }

        internal class RenewLeaseCommand : ITaskSeriesCommand
        {
            private readonly IStorageBlockBlob _leaseBlob;
            private readonly string _leaseId;
            private readonly string _lockId;
            private readonly IDelayStrategy _speedupStrategy;
            private readonly TraceWriter _trace;
            private DateTimeOffset _lastRenewal;
            private TimeSpan _lastRenewalLatency;
            private TimeSpan _leasePeriod;

            public RenewLeaseCommand(IStorageBlockBlob leaseBlob, string leaseId, string lockId, IDelayStrategy speedupStrategy, TraceWriter trace, TimeSpan leasePeriod)
            {
                _lastRenewal = DateTimeOffset.UtcNow;
                _leaseBlob = leaseBlob;
                _leaseId = leaseId;
                _lockId = lockId;
                _speedupStrategy = speedupStrategy;
                _trace = trace;
                _leasePeriod = leasePeriod;
            }

            public async Task<TaskSeriesCommandResult> ExecuteAsync(CancellationToken cancellationToken)
            {
                TimeSpan delay;

                try
                {
                    AccessCondition condition = new AccessCondition
                    {
                        LeaseId = _leaseId
                    };
                    DateTimeOffset requestStart = DateTimeOffset.UtcNow;
                    await _leaseBlob.RenewLeaseAsync(condition, null, null, cancellationToken);
                    _lastRenewal = DateTime.UtcNow;
                    _lastRenewalLatency = _lastRenewal - requestStart;

                    // The next execution should occur after a normal delay.
                    delay = _speedupStrategy.GetNextDelay(executionSucceeded: true);
                }
                catch (StorageException exception)
                {
                    if (exception.IsServerSideError())
                    {
                        // The next execution should occur more quickly (try to renew the lease before it expires).
                        delay = _speedupStrategy.GetNextDelay(executionSucceeded: false);
                        _trace.Warning(string.Format(CultureInfo.InvariantCulture, "Singleton lock renewal failed for blob '{0}' with error code {1}. Retry renewal in {2} milliseconds.",
                            _lockId, FormatErrorCode(exception), delay.TotalMilliseconds), source: TraceSource.Execution);
                    }
                    else
                    {
                        // Log the details we've been accumulating to help with debugging this scenario
                        int leasePeriodMilliseconds = (int)_leasePeriod.TotalMilliseconds;
                        string lastRenewalFormatted = _lastRenewal.ToString("yyyy-MM-ddTHH:mm:ss.FFFZ", CultureInfo.InvariantCulture);
                        int millisecondsSinceLastSuccess = (int)(DateTime.UtcNow - _lastRenewal).TotalMilliseconds;
                        int lastRenewalMilliseconds = (int)_lastRenewalLatency.TotalMilliseconds;

                        _trace.Error(string.Format(CultureInfo.InvariantCulture, "Singleton lock renewal failed for blob '{0}' with error code {1}. The last successful renewal completed at {2} ({3} milliseconds ago) with a duration of {4} milliseconds. The lease period was {5} milliseconds.",
                            _lockId, FormatErrorCode(exception), lastRenewalFormatted, millisecondsSinceLastSuccess, lastRenewalMilliseconds, leasePeriodMilliseconds));

                        // If we've lost the lease or cannot re-establish it, we want to fail any
                        // in progress function execution
                        throw;
                    }
                }
                return new TaskSeriesCommandResult(wait: Task.Delay(delay));
            }

            private static string FormatErrorCode(StorageException exception)
            {
                int statusCode;
                if (!exception.TryGetStatusCode(out statusCode))
                {
                    return "''";
                }

                string message = statusCode.ToString(CultureInfo.InvariantCulture);

                string errorCode = exception.GetErrorCode();

                if (errorCode != null)
                {
                    message += ": " + errorCode;
                }

                return message;
            }
        }
    }
}
