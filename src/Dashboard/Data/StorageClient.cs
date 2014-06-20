using Microsoft.WindowsAzure.Storage.Auth;

namespace Microsoft.Azure.Jobs.Host
{
    internal static class StorageClient
    {
        public static string GetAccountName(StorageCredentials credentials)
        {
            if (credentials == null)
            {
                return null;
            }

            return credentials.AccountName;
        }
    }
}
