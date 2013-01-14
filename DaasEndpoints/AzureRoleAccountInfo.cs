using System.Linq;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.ServiceRuntime;
using RunnerInterfaces;

namespace DaasEndpoints
{
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
                    _webDashboard = RoleEnvironment.GetConfigurationSettingValue("WebRoleEndpoint");
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
