using System;
using System.Globalization;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs
{
    internal class DefaultStorageValidator : IStorageValidator
    {
        /// <summary>
        /// Validate a Microsoft Azure Storage connection string, by parsing it, and placing
        /// a call to Azure to assert the credentials validity as well.
        /// </summary>
        public bool TryValidateConnectionString(string connectionString, out string validationErrorMessage)
        {
            if (String.IsNullOrEmpty(connectionString))
            {
                validationErrorMessage = "Microsoft Azure Storage account connection string is missing or empty.";
                return false;
            }

            // Will throw on parser errors. 
            CloudStorageAccount account;
            if (!CloudStorageAccount.TryParse(connectionString, out account))
            {
                validationErrorMessage = "Microsoft Azure Storage account connection string is not formatted correctly. Please visit http://msdn.microsoft.com/en-us/library/windowsazure/ee758697.aspx for details about configuring Microsoft Azure Storage connection strings.";
                return false;
            }

            if (IsDevelopmentStorageAccount(account))
            {
                validationErrorMessage = "The Microsoft Azure Storage Emulator is not supported, please use a Microsoft Azure Storage account hosted in Microsoft Azure.";
                return false;
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
            catch
            {
                validationErrorMessage = String.Format(CultureInfo.CurrentCulture,
                    "The account credentials for '{0}' are incorrect.",
                    account.Credentials.AccountName);
                return false;
            }

            validationErrorMessage = null;
            return true;
        }

        internal static bool IsDevelopmentStorageAccount(CloudStorageAccount account)
        {
            // see the section "Addressing local storage resources" in http://msdn.microsoft.com/en-us/library/windowsazure/hh403989.aspx 
            return account.BlobEndpoint.PathAndQuery.TrimStart('/') == account.Credentials.AccountName;
        }
    }
}
