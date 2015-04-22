// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
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

        private static async Task ValidateCredentialsAsyncCore(IStorageAccount account,
            CancellationToken cancellationToken)
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
                    "The account credentials for '{0}' are incorrect.", account.Credentials.AccountName);
                throw new InvalidOperationException(message);
            }
        }
    }
}
