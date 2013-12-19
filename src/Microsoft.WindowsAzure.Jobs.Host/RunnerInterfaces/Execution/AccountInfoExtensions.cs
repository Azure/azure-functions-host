namespace Microsoft.WindowsAzure.Jobs
{
    internal static class AccountInfoExtensions
    {
        public static CloudStorageAccount GetAccount(this IAccountInfo accountInfo)
        {
            var account = CloudStorageAccount.Parse(accountInfo.AccountConnectionString);
            return account;
        }

        public static string GetAccountName(this IAccountInfo accountInfo)
        {
            var account = accountInfo.GetAccount();
            return account.Credentials.AccountName;
        }
    }
}
