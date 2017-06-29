// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Globalization;
using Microsoft.WindowsAzure.Storage;

namespace Dashboard.Data
{
    public static class StorageAccountValidator
    {
        private const string HttpsEndpointScheme = "https";
        
        public static bool ValidateAccountAccessible(CloudStorageAccount account)
        {
            if (account == null)
            {
                throw new ArgumentNullException("account");
            }

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
                return false;
            }

            return true;
        }
        
        public static bool ValidateEndpointsSecure(CloudStorageAccount account)
        {
            if (account == null)
            {
                throw new ArgumentNullException("account");
            }

            if (!IsSecureEndpointProtocol(account.BlobEndpoint) ||
                !IsSecureEndpointProtocol(account.QueueEndpoint))
            {
                return false;
            }

            return true;
        }

        private static bool IsSecureEndpointProtocol(Uri endpoint)
        {
            return String.Equals(endpoint.Scheme, HttpsEndpointScheme, StringComparison.OrdinalIgnoreCase);
        }
    }
}