using System;
using System.Globalization;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs.Host.Executors
{
    internal class DefaultStorageCredentialsValidator : IStorageCredentialsValidator
    {
        public void ValidateCredentials(CloudStorageAccount account)
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
            catch
            {
                string message = String.Format(CultureInfo.CurrentCulture,
                    "The account credentials for '{0}' are incorrect.", account.Credentials.AccountName);
                throw new InvalidOperationException(message);
            }
        }
    }
}
