// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.WebJobs.Host.Executors
{
    internal class DefaultStorageCredentialsValidator : IStorageCredentialsValidator
    {
        public async Task ValidateCredentialsAsync(IStorageAccount account, CancellationToken cancellationToken)
        {
            CloudStorageAccount sdkAccount = account.SdkObject;

            // Verify the credentials are correct.
            // Have to actually ping a storage operation.
            var client = sdkAccount.CreateCloudBlobClient();

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
