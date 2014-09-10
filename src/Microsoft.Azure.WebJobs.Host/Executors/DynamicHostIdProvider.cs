// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Executors
{
    // Negotiates host ID based on the assembly name of the first indexed method (persists a GUID for this purpose).
    internal class DynamicHostIdProvider : IHostIdProvider
    {
        private readonly CloudBlobDirectory _directory;

        public DynamicHostIdProvider(CloudStorageAccount account)
            : this(VerifyNotNull(account).CreateCloudBlobClient().GetContainerReference(
                HostContainerNames.Hosts).GetDirectoryReference(HostDirectoryNames.Ids))
        {
        }

        private DynamicHostIdProvider(CloudBlobDirectory directory)
        {
            if (directory == null)
            {
                throw new ArgumentNullException("directory");
            }

            _directory = directory;
        }

        public Task<string> GetHostIdAsync(IEnumerable<MethodInfo> indexedMethods, CancellationToken cancellationToken)
        {
            string sharedHostName = GetSharedHostName(indexedMethods);
            return GetOrCreateHostIdAsync(sharedHostName, cancellationToken);
        }

        private string GetSharedHostName(IEnumerable<MethodInfo> indexedMethods)
        {
            // Determine the host name from the method list
            MethodInfo firstMethod = indexedMethods.FirstOrDefault();
            Assembly hostAssembly = firstMethod != null ? firstMethod.DeclaringType.Assembly : null;
            string hostName = hostAssembly != null ? hostAssembly.FullName : "Unknown";
            string sharedHostName = _directory.ServiceClient.Credentials.AccountName + "/" + hostName;
            return sharedHostName;
        }

        private async Task<string> GetOrCreateHostIdAsync(string sharedHostName, CancellationToken cancellationToken)
        {
            Debug.Assert(_directory != null);

            CloudBlockBlob blob = _directory.GetBlockBlobReference(sharedHostName);
            Guid? possibleHostId = await TryGetExistingIdAsync(blob, cancellationToken);

            if (possibleHostId.HasValue)
            {
                return possibleHostId.Value.ToString("N");
            }

            Guid newHostId = Guid.NewGuid();

            if (await TryInitializeIdAsync(blob, newHostId, cancellationToken))
            {
                return newHostId.ToString("N");
            }

            possibleHostId = await TryGetExistingIdAsync(blob, cancellationToken);

            if (possibleHostId.HasValue)
            {
                return possibleHostId.Value.ToString("N");
            }

            // Not expected - valid host ID didn't exist before, couldn't be created, and still didn't exist after.
            throw new InvalidOperationException("Unable to determine host ID.");
        }

        private static async Task<Guid?> TryGetExistingIdAsync(CloudBlockBlob blob, CancellationToken cancellationToken)
        {
            string text = await TryDownloadAsync(blob, cancellationToken);

            if (text == null)
            {
                return null;
            }

            Guid possibleHostId;

            if (Guid.TryParseExact(text, "N", out possibleHostId))
            {
                return possibleHostId;
            }

            return null;
        }

        private static async Task<string> TryDownloadAsync(CloudBlockBlob blob, CancellationToken cancellationToken)
        {
            try
            {
                return await blob.DownloadTextAsync(cancellationToken);
            }
            catch (StorageException exception)
            {
                if (exception.IsNotFound())
                {
                    return null;
                }
                else
                {
                    throw;
                }
            }
        }

        private static async Task<bool> TryInitializeIdAsync(CloudBlockBlob blob, Guid hostId,
            CancellationToken cancellationToken)
        {
            string text = hostId.ToString("N");
            AccessCondition accessCondition = new AccessCondition { IfNoneMatchETag = "*" };
            bool failedWithContainerNotFoundException = false;

            try
            {
                await blob.UploadTextAsync(text,
                    encoding: null,
                    accessCondition: accessCondition,
                    options: null,
                    operationContext: null,
                    cancellationToken: cancellationToken);
                return true;
            }
            catch (StorageException exception)
            {
                if (exception.IsPreconditionFailed() || exception.IsConflict())
                {
                    return false;
                }
                else if (exception.IsNotFoundContainerNotFound())
                {
                    failedWithContainerNotFoundException = true;
                }
                else
                {
                    throw;
                }
            }

            Debug.Assert(failedWithContainerNotFoundException);

            await blob.Container.CreateIfNotExistsAsync(cancellationToken);

            try
            {
                await blob.UploadTextAsync(text,
                    encoding: null,
                    accessCondition: accessCondition,
                    options: null,
                    operationContext: null,
                    cancellationToken: cancellationToken);
                return true;
            }
            catch (StorageException retryException)
            {
                if (retryException.IsPreconditionFailed() || retryException.IsConflict())
                {
                    return false;
                }
                else
                {
                    throw;
                }
            }
        }

        private static CloudStorageAccount VerifyNotNull(CloudStorageAccount account)
        {
            if (account == null)
            {
                throw new ArgumentNullException("account");
            }

            return account;
        }
    }
}
