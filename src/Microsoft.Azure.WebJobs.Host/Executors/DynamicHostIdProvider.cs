// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.WebJobs.Host.Executors
{
    // Negotiates host ID based on the assembly name of the first indexed method (persists a GUID for this purpose).
    internal class DynamicHostIdProvider : IHostIdProvider
    {
        private readonly IStorageAccountProvider _storageAccountProvider;
        private readonly Func<IFunctionIndexProvider> _getFunctionIndexProvider;

        public DynamicHostIdProvider(IStorageAccountProvider storageAccountProvider,
            Func<IFunctionIndexProvider> getFunctionIndexProvider)
        {
            if (storageAccountProvider == null)
            {
                throw new ArgumentNullException("storageAccountProvider");
            }

            if (getFunctionIndexProvider == null)
            {
                throw new ArgumentNullException("getFunctionIndexProvider");
            }

            _storageAccountProvider = storageAccountProvider;
            _getFunctionIndexProvider = getFunctionIndexProvider;
        }

        public async Task<string> GetHostIdAsync(CancellationToken cancellationToken)
        {
            IStorageAccount account;

            try
            {
                account = await _storageAccountProvider.GetStorageAccountAsync(cancellationToken);
            }
            catch (InvalidOperationException exception)
            {
                throw new InvalidOperationException(
                    "A host ID is required. Either set JobHostConfiguration.HostId or provide a valid storage " +
                    "connection string.", exception);
            }

            IFunctionIndex index = await _getFunctionIndexProvider.Invoke().GetAsync(cancellationToken);
            IEnumerable<MethodInfo> indexedMethods = index.ReadAllMethods();

            string sharedHostName = GetSharedHostName(indexedMethods, account);
            IStorageBlobDirectory directory = account.CreateBlobClient().GetContainerReference(
                HostContainerNames.Hosts).GetDirectoryReference(HostDirectoryNames.Ids);
            return await GetOrCreateHostIdAsync(sharedHostName, directory, cancellationToken);
        }

        private static string GetSharedHostName(IEnumerable<MethodInfo> indexedMethods, IStorageAccount storageAccount)
        {
            // Determine the host name from the method list
            MethodInfo firstMethod = indexedMethods.FirstOrDefault();
            Assembly hostAssembly = firstMethod != null ? firstMethod.DeclaringType.Assembly : null;
            string hostName = hostAssembly != null ? hostAssembly.FullName : "Unknown";
            string sharedHostName = storageAccount.Credentials.AccountName + "/" + hostName;
            return sharedHostName;
        }

        private static async Task<string> GetOrCreateHostIdAsync(string sharedHostName, IStorageBlobDirectory directory,
            CancellationToken cancellationToken)
        {
            Debug.Assert(directory != null);

            IStorageBlockBlob blob = directory.GetBlockBlobReference(sharedHostName);
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

        private static async Task<Guid?> TryGetExistingIdAsync(IStorageBlockBlob blob,
            CancellationToken cancellationToken)
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

        private static async Task<string> TryDownloadAsync(IStorageBlockBlob blob, CancellationToken cancellationToken)
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

        private static async Task<bool> TryInitializeIdAsync(IStorageBlockBlob blob, Guid hostId,
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
    }
}