using System.Linq;
using Microsoft.WindowsAzure;
using RunnerInterfaces;

namespace RunnerInterfaces
{
    // Provide underlying access to account information. 
    public interface IAccountInfo
    {
        // azure storage Account for the storage items the service uses to operate. 
        // This is a secret.
        string AccountConnectionString { get; }

        // URL prefix, can be used as API for doing stuff like queing calls via ICall.
        // This may be a WebRole running in the same Azure service instance. 
        // This can be public.
        string WebDashboardUri { get; }
    }

    public static class IAccountInfoExtensions
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

    // Default class for explicitly providing account information. 
    public class AccountInfo : IAccountInfo
    {
        public string AccountConnectionString { get; set; }
        public string WebDashboardUri { get; set; }
    }
}