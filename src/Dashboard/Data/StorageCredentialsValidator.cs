// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Globalization;
using Microsoft.WindowsAzure.Storage;

namespace Dashboard.Data
{
    public class StorageCredentialsValidator
    {
        [CLSCompliant(false)]
        public static void ValidateCredentials(CloudStorageAccount account)
        {
            // Verify the credentials are correct.
            // Have to actually ping a storage operation.
            var client = account.CreateCloudBlobClient();

            try
            {
                // This can hang for a long time if the account name is wrong. 
                // If will fail fast if the password is incorrect.
                client.GetServiceProperties();
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