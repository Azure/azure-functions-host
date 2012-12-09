using System.Linq;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.ServiceRuntime;
using RunnerInterfaces;

namespace DaasEndpoints
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

    // Get account information via the Azure role Configuration. 
    public class AzureRoleAccountInfo : IAccountInfo
    {
        //public const string WebDashboardUri = @"http://localhost:44498/";
        // Something like. @"http://daas3.azurewebsites.net/";
        // But need to get IP address from the role instances.
        private string _webDashboard;

        public string WebDashboardUri
        {
            get
            {
                if (_webDashboard == null)
                {
                    // $$$ Better way to discover this endpoint? Make sure we're really on the Http listener. 
                    var i = RoleEnvironment.Roles["WebFrontEnd"];
                    RoleInstance x = i.Instances.First();
                    RoleInstanceEndpoint endpoint = x.InstanceEndpoints.First().Value;

                    _webDashboard = string.Format("{0}://{1}/", endpoint.Protocol, endpoint.IPEndpoint);
                }
                return _webDashboard;
            }
        }

        private string _accountConnectionString;
        public string AccountConnectionString
        {
            get
            {
                if (_accountConnectionString == null)
                {
                    _accountConnectionString = RoleEnvironment.GetConfigurationSettingValue("MainStorage");
                }
                return _accountConnectionString;
            }
        }
    }
}
