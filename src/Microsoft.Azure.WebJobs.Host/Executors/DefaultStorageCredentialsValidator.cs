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
        private StorageCredentials _primaryCredentials = null;

        public async Task ValidateCredentialsAsync(IStorageAccount account, bool isPrimaryAccount, CancellationToken cancellationToken)
        {
            if (account == null)
            {
                throw new ArgumentNullException("account");
            }

            StorageCredentials credentials = account.Credentials;

            // Avoid double-validating the same account and credentials.
            if ((!isPrimaryAccount && _validatedCredentials.Contains(credentials)) || _primaryCredentials == credentials)
            {
                return;
            }

            await ValidateCredentialsAsyncCore(account, isPrimaryAccount, cancellationToken);
            _validatedCredentials.Add(credentials);
        }

        private async Task ValidateCredentialsAsyncCore(IStorageAccount account,
            bool isPrimaryAccount, CancellationToken cancellationToken)
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
            catch
            {
                string message = String.Format(CultureInfo.CurrentCulture,
                    "Invalid storage account '{0}'. Please make sure your credentials are correct.",
                    account.Credentials.AccountName);
                throw new InvalidOperationException(message);
            }

            if (isPrimaryAccount)
            {
                // Primary storage accounts require Queues
                IStorageQueueClient queueClient = account.CreateQueueClient();
                IStorageQueue queue = queueClient.GetQueueReference("name");
                try
                {
                    await queue.ExistsAsync(cancellationToken);
                    _primaryCredentials = account.Credentials;
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
                        string message = String.Format(CultureInfo.CurrentCulture,
                            "Invalid storage account '{0}'. Primary storage accounts must be general "
                            + "purpose accounts and not restricted blob storage accounts.", account.Credentials.AccountName);
                        throw new InvalidOperationException(message);
                    }
                    throw;
                }
            }
        }
    }
}
