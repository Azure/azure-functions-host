// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Microsoft.Azure.WebJobs.Host.Timers;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host
{
    // Provides a blob-leased based implementation 
    internal class BlobLeaseDistributedLockManager : IDistributedLockManager
    {
        internal const string FunctionInstanceMetadataKey = "FunctionInstance";

        private readonly IStorageAccountProvider _accountProvider;
        private readonly ConcurrentDictionary<string, IStorageBlobDirectory> _lockDirectoryMap = new ConcurrentDictionary<string, IStorageBlobDirectory>(StringComparer.OrdinalIgnoreCase);
                
        private readonly TraceWriter _trace;
        private readonly ILogger _logger;
      
        public BlobLeaseDistributedLockManager(
            IStorageAccountProvider accountProvider,
            TraceWriter trace,
            ILogger logger)
        {
            _accountProvider = accountProvider;
            _trace = trace;
            _logger = logger;
        }

        public Task<bool> RenewAsync(IDistributedLock lockHandle, CancellationToken cancellationToken)
        {
            SingletonLockHandle singletonLockHandle = (SingletonLockHandle)lockHandle;
            return singletonLockHandle.RenewAsync(_trace, _logger, cancellationToken);
        }

        public async Task ReleaseLockAsync(IDistributedLock lockHandle, CancellationToken cancellationToken)
        {
            SingletonLockHandle singletonLockHandle = (SingletonLockHandle)lockHandle;
            await ReleaseLeaseAsync(singletonLockHandle.Blob, singletonLockHandle.LeaseId, cancellationToken);
        }

        public async virtual Task<string> GetLockOwnerAsync(string account, string lockId, CancellationToken cancellationToken)
        {
            IStorageBlobDirectory lockDirectory = GetLockDirectory(account);
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

        public async Task<IDistributedLock> TryLockAsync(
            string account,
            string lockId,
            string lockOwnerId,
            string proposedLeaseId,
            TimeSpan lockPeriod,
            CancellationToken cancellationToken)
        {
            IStorageBlobDirectory lockDirectory = GetLockDirectory(account);
            IStorageBlockBlob lockBlob = lockDirectory.GetBlockBlobReference(lockId);
            string leaseId = await TryAcquireLeaseAsync(lockBlob, lockPeriod, proposedLeaseId, cancellationToken);

            if (string.IsNullOrEmpty(leaseId))
            {
                return null;
            }

            if (!string.IsNullOrEmpty(lockOwnerId))
            {
                await WriteLeaseBlobMetadata(lockBlob, leaseId, lockOwnerId, cancellationToken);
            }

            SingletonLockHandle lockHandle = new SingletonLockHandle(leaseId, lockId, lockBlob, lockPeriod);
                                    
            return lockHandle;
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
                Task<IStorageAccount> task = _accountProvider.GetStorageAccountAsync(accountName, CancellationToken.None);
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

        private static async Task<string> TryAcquireLeaseAsync(
            IStorageBlockBlob blob, 
            TimeSpan leasePeriod, 
            string proposedLeaseId,
            CancellationToken cancellationToken)
        {
            bool blobDoesNotExist = false;
            try
            {
                // Optimistically try to acquire the lease. The blob may not yet
                // exist. If it doesn't we handle the 404, create it, and retry below
                return await blob.AcquireLeaseAsync(leasePeriod, proposedLeaseId, cancellationToken);
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

            var container = blob.Container;            
            try
            {
                await container.CreateIfNotExistsAsync(cancellationToken);
            }
            catch (StorageException exc)
            when (exc.RequestInformation.HttpStatusCode == 409 && string.Compare("ContainerBeingDeleted", exc.RequestInformation.ExtendedErrorInformation?.ErrorCode) == 0)
            {
                throw new StorageException("The host container is pending deletion and currently inaccessible.");
            }

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

        internal class SingletonLockHandle : IDistributedLock
        {
            private readonly TimeSpan _leasePeriod;

            private DateTimeOffset _lastRenewal;
            private TimeSpan _lastRenewalLatency;

            public SingletonLockHandle()
            {
            }

            public SingletonLockHandle(string leaseId, string lockId, IStorageBlockBlob blob, TimeSpan leasePeriod)
            {
                this.LeaseId = leaseId;
                this.LockId = lockId;
                this._leasePeriod = leasePeriod;
                this.Blob = blob;
            }

            public string LeaseId { get; internal set; }
            public string LockId { get; internal set; }
            public IStorageBlockBlob Blob { get; internal set; }

            public async Task<bool> RenewAsync(TraceWriter trace, ILogger logger, CancellationToken cancellationToken)
            {
                try
                {
                    AccessCondition condition = new AccessCondition
                    {
                        LeaseId = this.LeaseId
                    };
                    DateTimeOffset requestStart = DateTimeOffset.UtcNow;
                    await this.Blob.RenewLeaseAsync(condition, null, null, cancellationToken);
                    _lastRenewal = DateTime.UtcNow;
                    _lastRenewalLatency = _lastRenewal - requestStart;

                    // The next execution should occur after a normal delay.
                    return true;
                }
                catch (StorageException exception)
                {
                    if (exception.IsServerSideError())
                    {
                        string msg = string.Format(CultureInfo.InvariantCulture, "Singleton lock renewal failed for blob '{0}' with error code {1}.",
                            this.LockId, FormatErrorCode(exception));
                        trace.Warning(msg, source: TraceSource.Execution);
                        logger?.LogWarning(msg);
                        return false; // The next execution should occur more quickly (try to renew the lease before it expires).
                    }
                    else
                    {
                        // Log the details we've been accumulating to help with debugging this scenario
                        int leasePeriodMilliseconds = (int)_leasePeriod.TotalMilliseconds;
                        string lastRenewalFormatted = _lastRenewal.ToString("yyyy-MM-ddTHH:mm:ss.FFFZ", CultureInfo.InvariantCulture);
                        int millisecondsSinceLastSuccess = (int)(DateTime.UtcNow - _lastRenewal).TotalMilliseconds;
                        int lastRenewalMilliseconds = (int)_lastRenewalLatency.TotalMilliseconds;

                        string msg = string.Format(CultureInfo.InvariantCulture, "Singleton lock renewal failed for blob '{0}' with error code {1}. The last successful renewal completed at {2} ({3} milliseconds ago) with a duration of {4} milliseconds. The lease period was {5} milliseconds.",
                            this.LockId, FormatErrorCode(exception), lastRenewalFormatted, millisecondsSinceLastSuccess, lastRenewalMilliseconds, leasePeriodMilliseconds);
                        trace.Error(msg);
                        logger?.LogError(msg);
                        
                        // If we've lost the lease or cannot re-establish it, we want to fail any
                        // in progress function execution
                        throw;
                    }
                }                
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