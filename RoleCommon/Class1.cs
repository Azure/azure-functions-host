using System.Linq;
using Microsoft.WindowsAzure.ServiceRuntime;
using RunnerInterfaces;

namespace RoleCommon
{
    public class AzureRoleAccountInfo : IAccountInfo
    {

        //public const string WebDashboardUri = @"http://localhost:44498/";
        // Something like. @"http://daas3.azurewebsites.net/";
        // But need to get IP address from the role instances.
        private static string _webDashboard;

        public static string WebDashboardUri
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

        private static string _accountConnectionString;
        public static string AccountConnectionString
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
