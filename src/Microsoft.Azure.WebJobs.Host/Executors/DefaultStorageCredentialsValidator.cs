// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;

namespace Microsoft.Azure.WebJobs.Host.Executors
{
    internal class DefaultStorageCredentialsValidator : IStorageCredentialsValidator
    {
        private readonly HashSet<StorageCredentials> _validatedCredentials = new HashSet<StorageCredentials>();

        public async Task ValidateCredentialsAsync(IStorageAccount account, CancellationToken cancellationToken)
        {
            if (account == null)
            {
                throw new ArgumentNullException("account");
            }

            StorageCredentials credentials = account.Credentials;

            // Avoid double-validating the same account and credentials.
            if (_validatedCredentials.Contains(credentials))
            {
                return;
            }

            await ValidateCredentialsAsyncCore(account, cancellationToken);
            _validatedCredentials.Add(credentials);
        }

        // Test that the credentials are valid and classify the account.Type as one of StorageAccountTypes
        private async Task ValidateCredentialsAsyncCore(IStorageAccount account, CancellationToken cancellationToken)
        {
            // Verify the credentials are correct.
            // Have to actually ping a storage operation.
            IStorageBlobClient client = account.CreateBlobClient();

            try
            {
                // This can hang for a long time if the account name is wrong. 
                // If will fail fast if the password is incorrect.
                await client.GetServicePropertiesAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                var storageException = e as StorageException;
                if (storageException != null && storageException.RequestInformation != null &&
                    storageException.RequestInformation.HttpStatusCode == 400 &&
                    storageException.RequestInformation.ExtendedErrorInformation.ErrorCode == "InvalidQueryParameterValue")
                {
                    // Premium storage accounts do not support the GetServicePropertiesAsync call, and respond with a 400 'InvalidQueryParameterValue'.
                    // If we see this error response classify the account as a premium account
                    account.Type = StorageAccountType.Premium;
                    return;
                }
                else
                {
                    // If not a recognized error, the credentials are invalid
                    string message = String.Format(CultureInfo.CurrentCulture,
                        "Invalid storage account '{0}'. Please make sure your credentials are correct.",
                        account.Credentials.AccountName);
                    throw new InvalidOperationException(message);
                }
            }

            IStorageQueueClient queueClient = account.CreateQueueClient();
            IStorageQueue queue = queueClient.GetQueueReference("name");
            try
            {
                await queue.ExistsAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (StorageException exception)
            {
                WebException webException = exception.GetBaseException() as WebException;
                if (webException != null && webException.Status == WebExceptionStatus.NameResolutionFailure)
                {
                    // Blob-only storage accounts do not support services other than Blob.  
                    // If we see a name resolution failure on the queue endpoint classify as a blob-only account
                    account.Type = StorageAccountType.BlobOnly;
                    return;
                }
                throw;
            }
        }
    }
}
