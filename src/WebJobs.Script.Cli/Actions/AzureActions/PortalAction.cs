using System;
using System.Diagnostics;
using System.Threading.Tasks;
using WebJobs.Script.Cli.Arm;

namespace WebJobs.Script.Cli.Actions.AzureActions
{
    [Action(Name = "portal", Context = Context.Azure, HelpText = "Launch default browser with link to the current app in https://portal.azure.com")]
    class PortalAction : BaseFunctionAppAction
    {
        private readonly IArmManager _armManager;

        public PortalAction(IArmManager armManager)
        {
            _armManager = armManager;
        }

        public override async Task RunAsync()
        {
            var functionApp = await _armManager.GetFunctionAppAsync(FunctionAppName);
            var currentTenant = await _armManager.GetCurrentTenantAsync();
            var portalHostName = "https://portal.azure.com";
            Process.Start($"{portalHostName}/{currentTenant.domain}#resource{functionApp.ArmId}");
        }
    }
}
