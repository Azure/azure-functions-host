// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// TODO: TEMP - implementation should be moved https://github.com/Azure/azure-webjobs-sdk/issues/2710
    /// This is an overridden implementation based off the StorageBaseDistributedLockManager in Microsoft.Azure.WebJobs.Host.Storage package.
    /// Provides a BlobClient lease-based implementation of the <see cref="IDistributedLockManager"/> service for singleton locking.
    /// </summary>
    internal class BlobLeaseDistributedLockManager : IDistributedLockManager
    {
        internal const string FunctionInstanceMetadataKey = "FunctionInstance";
        internal const string SingletonLocks = "locks";

        private readonly ILogger _logger;
        private readonly IAzureStorageProvider _azureStorageProvider;

        public BlobLeaseDistributedLockManager(
            ILoggerFactory loggerFactory,
            IAzureStorageProvider azureStorageProvider) // Take an ILoggerFactory since that's a DI component.
        {
            _logger = loggerFactory.CreateLogger(LogCategories.Singleton);
            _azureStorageProvider = azureStorageProvider;
        }

        public Task<bool> RenewAsync(IDistributedLock lockHandle, CancellationToken cancellationToken)
        {
            SingletonLockHandle singletonLockHandle = (SingletonLockHandle)lockHandle;
            return singletonLockHandle.RenewAsync(_logger, cancellationToken);
        }

        public async Task ReleaseLockAsync(IDistributedLock lockHandle, CancellationToken cancellationToken)
        {
            SingletonLockHandle singletonLockHandle = (SingletonLockHandle)lockHandle;
            await ReleaseLeaseAsync(singletonLockHandle.BlobLeaseClient, singletonLockHandle.LeaseId, cancellationToken);
        }

        public async virtual Task<string> GetLockOwnerAsync(string account, string lockId, CancellationToken cancellationToken)
        {
            var lockBlob = this.GetContainerClient(account).GetBlobClient(GetLockPath(lockId));

            var blobProperties = await ReadLeaseBlobMetadata(lockBlob, cancellationToken);

            // if the lease is Available, then there is no current owner
            // (any existing owner value is the last owner that held the lease)
            if (blobProperties != null &&
                blobProperties.LeaseState == LeaseState.Available &&
                blobProperties.LeaseStatus == LeaseStatus.Unlocked)
            {
                return null;
            }

            blobProperties.Metadata.TryGetValue(FunctionInstanceMetadataKey, out string owner);
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
            var lockBlob = this.GetContainerClient(account).GetBlobClient(GetLockPath(lockId));
            string leaseId = await TryAcquireLeaseAsync(this, lockBlob, lockPeriod, proposedLeaseId, cancellationToken, account);

            if (string.IsNullOrEmpty(leaseId))
            {
                return null;
            }

            if (!string.IsNullOrEmpty(lockOwnerId))
            {
                await WriteLeaseBlobMetadata(lockBlob, leaseId, lockOwnerId, cancellationToken);
            }

            SingletonLockHandle lockHandle = new SingletonLockHandle(leaseId, lockId, this.GetBlobLeaseClient(lockBlob, leaseId), lockPeriod);

            return lockHandle;
        }

        protected virtual BlobContainerClient GetContainerClient(string accountName)
        {
            if (!string.IsNullOrWhiteSpace(accountName))
            {
                throw new InvalidOperationException("Must replace singleton lease manager to support multiple accounts");
            }

            return _azureStorageProvider.GetBlobContainerClient();
        }

        internal string GetLockPath(string lockId)
        {
            // lockId here is already in the format {accountName}/{functionDescriptor}.{scopeId}
            return string.Format(CultureInfo.InvariantCulture, "{0}/{1}", SingletonLocks, lockId);
        }

        // Allows the extension method to be mocked for testing
        protected virtual BlobLeaseClient GetBlobLeaseClient(BlobClient blobClient, string proposedLeaseId)
        {
            return blobClient.GetBlobLeaseClient(proposedLeaseId);
        }

        private static async Task<string> TryAcquireLeaseAsync(
            BlobLeaseDistributedLockManager lockManager,
            BlobClient blobClient,
            TimeSpan leasePeriod,
            string proposedLeaseId,
            CancellationToken cancellationToken,
            string accountOverride)
        {
            bool blobDoesNotExist = false;
            try
            {
                // Optimistically try to acquire the lease. The blob may not yet
                // exist. If it doesn't we handle the 404, create it, and retry below
                var leaseResponse = await lockManager.GetBlobLeaseClient(blobClient, proposedLeaseId).AcquireAsync(leasePeriod, cancellationToken: cancellationToken);
                return leaseResponse.Value.LeaseId;
                // return await blob.AcquireLeaseAsync(leasePeriod, proposedLeaseId, accessCondition: null, options: null, operationContext: null, cancellationToken: cancellationToken);
            }
            catch (RequestFailedException exception)
            {
                if (exception.Status == 409)
                {
                    return null;
                }
                else if (exception.Status == 404)
                {
                    blobDoesNotExist = true;
                }
                else
                {
                    throw;
                }
            }

            if (blobDoesNotExist)
            {
                await TryCreateAsync(lockManager, blobClient, cancellationToken, accountOverride);

                try
                {
                    var leaseResponse = await lockManager.GetBlobLeaseClient(blobClient, proposedLeaseId).AcquireAsync(leasePeriod, cancellationToken: cancellationToken);
                    return leaseResponse.Value.LeaseId;
                }
                catch (RequestFailedException exception)
                {
                    if (exception.Status == 409)
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

        private static async Task ReleaseLeaseAsync(BlobLeaseClient blobLeaseClient, string leaseId, CancellationToken cancellationToken)
        {
            try
            {
                // Note that this call returns without throwing if the lease is expired. See the table at:
                // http://msdn.microsoft.com/en-us/library/azure/ee691972.aspx
                await blobLeaseClient.ReleaseAsync(cancellationToken: cancellationToken);
            }
            catch (RequestFailedException exception)
            {
                if (exception.Status == 404 ||
                    exception.Status == 409)
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
        }

        private static async Task<bool> TryCreateAsync(BlobLeaseDistributedLockManager lockManager, BlobClient blob, CancellationToken cancellationToken, string accountOverride)
        {
            bool isContainerNotFoundException = false;

            try
            {
                using (Stream stream = new MemoryStream(Encoding.UTF8.GetBytes(string.Empty)))
                {
                    await blob.UploadAsync(stream, cancellationToken: cancellationToken);
                }
                return true;
            }
            catch (RequestFailedException exception)
            {
                if (exception.Status == 404)
                {
                    isContainerNotFoundException = true;
                }
                else if (exception.Status == 409 ||
                            exception.Status == 412)
                {
                    // The blob already exists, or is leased by someone else
                    return false;
                }
                else
                {
                    throw;
                }
            }

            Debug.Assert(isContainerNotFoundException);

            var container = lockManager.GetContainerClient(accountOverride);
            try
            {
                await container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
            }
            catch (RequestFailedException exception)
            when (exception.Status == 409 && string.Compare("ContainerBeingDeleted", exception.ErrorCode) == 0)
            {
                throw new RequestFailedException("The host container is pending deletion and currently inaccessible.");
            }

            try
            {
                using (Stream stream = new MemoryStream(Encoding.UTF8.GetBytes(string.Empty)))
                {
                    await blob.UploadAsync(stream, cancellationToken: cancellationToken);
                }

                return true;
            }
            catch (RequestFailedException exception)
            {
                if (exception.Status == 409 || exception.Status == 412)
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

        private static async Task WriteLeaseBlobMetadata(BlobClient blobClient, string leaseId, string functionInstanceId, CancellationToken cancellationToken)
        {
            var blobProperties = await ReadLeaseBlobMetadata(blobClient, cancellationToken);
            if (blobProperties != null)
            {
                blobProperties.Metadata[FunctionInstanceMetadataKey] = functionInstanceId;
                await blobClient.SetMetadataAsync(
                    blobProperties.Metadata,
                    new BlobRequestConditions { LeaseId = leaseId },
                    cancellationToken: cancellationToken);
            }
        }

        private static async Task<BlobProperties> ReadLeaseBlobMetadata(BlobClient blobClient, CancellationToken cancellationToken)
        {
            try
            {
                var propertiesResponse = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);
                return propertiesResponse.Value;
            }
            catch (RequestFailedException exception)
            {
                if (exception.Status == 404)
                {
                    // the blob no longer exists
                    return null;
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

            public SingletonLockHandle(string leaseId, string lockId, BlobLeaseClient blobLeaseClient, TimeSpan leasePeriod)
            {
                this.LeaseId = leaseId;
                this.LockId = lockId;
                this._leasePeriod = leasePeriod;
                this.BlobLeaseClient = blobLeaseClient;
            }

            public string LeaseId { get; internal set; }

            public string LockId { get; internal set; }

            public BlobLeaseClient BlobLeaseClient { get; internal set; }

            public async Task<bool> RenewAsync(ILogger logger, CancellationToken cancellationToken)
            {
                try
                {
                    DateTimeOffset requestStart = DateTimeOffset.UtcNow;
                    await this.BlobLeaseClient.RenewAsync(cancellationToken: cancellationToken);
                    _lastRenewal = DateTime.UtcNow;
                    _lastRenewalLatency = _lastRenewal - requestStart;

                    // The next execution should occur after a normal delay.
                    return true;
                }
                catch (RequestFailedException exception)
                {
                    // indicates server-side error
                    if (exception.Status >= 500 && exception.Status < 600)
                    {
                        string msg = string.Format(CultureInfo.InvariantCulture, "Singleton lock renewal failed for blob '{0}' with error code {1}.",
                            this.LockId, FormatErrorCode(exception));
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
                        logger?.LogError(msg);

                        // If we've lost the lease or cannot re-establish it, we want to fail any
                        // in progress function execution
                        throw;
                    }
                }
            }

            private static string FormatErrorCode(RequestFailedException exception)
            {
                string message = exception.Status.ToString(CultureInfo.InvariantCulture);

                string errorCode = exception.ErrorCode;

                if (errorCode != null)
                {
                    message += ": " + errorCode;
                }

                return message;
            }
        }
    }
}
