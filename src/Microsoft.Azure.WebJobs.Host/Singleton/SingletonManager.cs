// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Bindings.Path;
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
        private readonly IBackgroundExceptionDispatcher _backgroundExceptionDispatcher;
        private readonly SingletonConfiguration _config;
        private IStorageBlobDirectory _directory;
        private TimeSpan _minimumLeaseRenewalInterval = TimeSpan.FromSeconds(1);
        private TraceWriter _trace;

        // For mock testing only
        internal SingletonManager()
        {
        }

        public SingletonManager(IStorageBlobClient blobClient, IBackgroundExceptionDispatcher backgroundExceptionDispatcher, SingletonConfiguration config, TraceWriter trace, INameResolver nameResolver = null)
        {
            _nameResolver = nameResolver;
            _backgroundExceptionDispatcher = backgroundExceptionDispatcher;
            _directory = blobClient.GetContainerReference(HostContainerNames.Hosts)
                                   .GetDirectoryReference(HostDirectoryNames.SingletonLocks);
            _config = config;
            _trace = trace;
        }

        internal virtual SingletonConfiguration Config
        {
            get
            {
                return _config;
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

        public async virtual Task<object> TryLockAsync(string lockId, string functionInstanceId, SingletonAttribute attribute, CancellationToken cancellationToken)
        {
            IStorageBlockBlob lockBlob = _directory.GetBlockBlobReference(lockId);
            await TryCreateAsync(lockBlob, cancellationToken);

            _trace.Verbose(string.Format(CultureInfo.InvariantCulture, "Waiting for Singleton lock ({0})", lockId), source: TraceSource.Execution);

            string leaseId = await TryAcquireLeaseAsync(lockBlob, _config.LockPeriod, cancellationToken);
            if (string.IsNullOrEmpty(leaseId))
            {
                // Someone else has the lease. Continue trying to periodically get the lease for
                // a period of time
                TimeSpan acquisitionTimeout = attribute.LockAcquisitionTimeout != null
                    ? TimeSpan.FromSeconds(attribute.LockAcquisitionTimeout.Value) :
                    _config.LockAcquisitionTimeout;
                double remainingWaitTime = acquisitionTimeout.TotalMilliseconds;
                while (string.IsNullOrEmpty(leaseId) && remainingWaitTime > 0)
                {
                    await Task.Delay(_config.LockAcquisitionPollingInterval);
                    leaseId = await TryAcquireLeaseAsync(lockBlob, _config.LockPeriod, cancellationToken);
                    remainingWaitTime -= _config.LockAcquisitionPollingInterval.TotalMilliseconds;
                }
            }

            if (string.IsNullOrEmpty(leaseId))
            {
                _trace.Verbose(string.Format(CultureInfo.InvariantCulture, "Unable to acquire Singleton lock ({0}).", lockId), source: TraceSource.Execution);
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
                LeaseRenewalTimer = CreateLeaseRenewalTimer(lockBlob, leaseId, lockId, _config.LockPeriod, _backgroundExceptionDispatcher)
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

        public static string FormatLockId(MethodInfo method, string scope)
        {
            string lockId = string.Format(CultureInfo.InvariantCulture, "{0}.{1}", method.DeclaringType.FullName, method.Name);
            if (!string.IsNullOrEmpty(scope))
            {
                lockId += "." + scope;
            }
            return lockId;
        }

        public string GetBoundScope(string scope, IReadOnlyDictionary<string, object> bindingData)
        {
            if (_nameResolver != null)
            {
                scope = _nameResolver.ResolveWholeString(scope);
            }

            if (bindingData != null)
            {
                BindingTemplate bindingTemplate = BindingTemplate.FromString(scope);
                IReadOnlyDictionary<string, string> parameters = BindingDataPathHelper.ConvertParameters(bindingData);
                return bindingTemplate.Bind(parameters);
            }
            else
            {
                return scope;
            }
        }

        public async virtual Task<string> GetLockOwnerAsync(string lockId, CancellationToken cancellationToken)
        {
            IStorageBlockBlob lockBlob = _directory.GetBlockBlobReference(lockId);

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

        private ITaskSeriesTimer CreateLeaseRenewalTimer(IStorageBlockBlob leaseBlob, string leaseId, string lockId, TimeSpan leasePeriod, 
            IBackgroundExceptionDispatcher backgroundExceptionDispatcher)
        {
            // renew the lease when it is halfway to expiring   
            TimeSpan normalUpdateInterval = new TimeSpan(leasePeriod.Ticks / 2);

            IDelayStrategy speedupStrategy = new LinearSpeedupStrategy(normalUpdateInterval, MinimumLeaseRenewalInterval);
            ITaskSeriesCommand command = new RenewLeaseCommand(leaseBlob, leaseId, lockId, speedupStrategy, _trace);
            return new TaskSeriesTimer(command, backgroundExceptionDispatcher, Task.Delay(normalUpdateInterval));
        }

        private async Task<string> TryAcquireLeaseAsync(IStorageBlockBlob blob, TimeSpan leasePeriod, CancellationToken cancellationToken)
        {
            try
            {
                return await blob.AcquireLeaseAsync(leasePeriod, null, cancellationToken);
            }
            catch (StorageException exception)
            {
                if (exception.IsConflictLeaseAlreadyPresent())
                {
                    return null;
                }
                else if (exception.IsNotFoundBlobOrContainerNotFound())
                {
                    // If someone deleted the receipt, there's no lease to acquire.
                    return null;
                }
                else
                {
                    throw;
                }
            }
        }

        private async Task ReleaseLeaseAsync(IStorageBlockBlob blob, string leaseId, CancellationToken cancellationToken)
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
                if (exception.IsNotFoundBlobOrContainerNotFound())
                {
                    // The user deleted the receipt or its container; nothing to release at this point.
                }
                else if (exception.IsConflictLeaseIdMismatchWithLeaseOperation())
                {
                    // Another lease is active; nothing for this lease to release at this point.
                }
                else
                {
                    throw;
                }
            }
        }

        private async Task<bool> TryCreateAsync(IStorageBlockBlob blob, CancellationToken cancellationToken)
        {
            AccessCondition accessCondition = new AccessCondition { IfNoneMatchETag = "*" };
            bool isContainerNotFoundException = false;

            try
            {
                await blob.UploadTextAsync(String.Empty,
                    encoding: null,
                    accessCondition: accessCondition,
                    options: null,
                    operationContext: null,
                    cancellationToken: cancellationToken);
                return true;
            }
            catch (StorageException exception)
            {
                if (exception.IsNotFoundContainerNotFound())
                {
                    isContainerNotFoundException = true;
                }
                else if (exception.IsConflictBlobAlreadyExists())
                {
                    return false;
                }
                else if (exception.IsPreconditionFailedLeaseIdMissing())
                {
                    return false;
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
                await blob.UploadTextAsync(String.Empty,
                    encoding: null,
                    accessCondition: accessCondition,
                    options: null,
                    operationContext: null,
                    cancellationToken: cancellationToken);
                return true;
            }
            catch (StorageException exception)
            {
                if (exception.IsConflictBlobAlreadyExists())
                {
                    return false;
                }
                else if (exception.IsPreconditionFailedLeaseIdMissing())
                {
                    return false;
                }
                else
                {
                    throw;
                }
            }
        }

        private async Task WriteLeaseBlobMetadata(IStorageBlockBlob blob, string leaseId, string functionInstanceId, CancellationToken cancellationToken)
        {
            blob.Metadata.Add(FunctionInstanceMetadataKey, functionInstanceId);

            try
            {
                await blob.SetMetadataAsync(
                    accessCondition: new AccessCondition { LeaseId = leaseId },
                    options: null,
                    operationContext: null,
                    cancellationToken: cancellationToken);
            }
            catch (StorageException exception)
            {
                if (exception.IsNotFoundBlobOrContainerNotFound())
                {
                    // The user deleted the receipt or its container;
                }
                else if (exception.IsPreconditionFailedLeaseLost())
                {
                    // The lease expired;
                }
                else
                {
                    throw;
                }
            }
        }

        private async Task ReadLeaseBlobMetadata(IStorageBlockBlob blob, CancellationToken cancellationToken)
        {
            try
            {
                await blob.FetchAttributesAsync(cancellationToken);
            }
            catch (StorageException exception)
            {
                if (exception.IsNotFoundBlobOrContainerNotFound())
                {
                    // The user deleted the receipt or its container;
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

            public RenewLeaseCommand(IStorageBlockBlob leaseBlob, string leaseId, string lockId, IDelayStrategy speedupStrategy, TraceWriter trace)
            {
                _leaseBlob = leaseBlob;
                _leaseId = leaseId;
                _lockId = lockId;
                _speedupStrategy = speedupStrategy;
                _trace = trace;
            }

            public async Task<TaskSeriesCommandResult> ExecuteAsync(CancellationToken cancellationToken)
            {
                TimeSpan delay;

                try
                {
                    _trace.Verbose(string.Format(CultureInfo.InvariantCulture, "Renewing Singleton lock ({0})", _lockId), source: TraceSource.Execution);

                    AccessCondition condition = new AccessCondition
                    {
                        LeaseId = _leaseId
                    };
                    await _leaseBlob.RenewLeaseAsync(condition, null, null, cancellationToken);

                    // The next execution should occur after a normal delay.
                    delay = _speedupStrategy.GetNextDelay(executionSucceeded: true);
                }
                catch (StorageException exception)
                {
                    if (exception.IsServerSideError())
                    {
                        // The next execution should occur more quickly (try to renew the lease before it expires).
                        delay = _speedupStrategy.GetNextDelay(executionSucceeded: false);
                    }
                    else
                    {
                        // If we've lost the lease or cannot restablish it, we want to fail any
                        // in progress function execution
                        throw;
                    }
                }

                return new TaskSeriesCommandResult(wait: Task.Delay(delay));
            }
        }
    }
}
