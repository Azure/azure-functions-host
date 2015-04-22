// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Globalization;
using Microsoft.WindowsAzure.Storage;

namespace Dashboard.Data
{
    public class StorageAccountValidator
    {
        private const string HttpsEndpointScheme = "https";

        [CLSCompliant(false)]
        public static bool ValidateAccountAccessible(CloudStorageAccount account)
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
                return false;
            }

            return true;
        }

        [CLSCompliant(false)]
        public static bool ValidateEndpointsSecure(CloudStorageAccount account)
        {
            if (!IsSecureEndpointProtocol(account.BlobEndpoint) ||
                !IsSecureEndpointProtocol(account.QueueEndpoint))
            {
                return false;
            }

            return true;
        }

        private static bool IsSecureEndpointProtocol(Uri endpoint)
        {
            return String.Equals(endpoint.Scheme, HttpsEndpointScheme, StringComparison.InvariantCultureIgnoreCase);
        }
    }
}