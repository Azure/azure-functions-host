using System.Diagnostics;
using System.Threading.Tasks;

namespace WebJobs.Script.ConsoleHost.Commands
{
    public class OpenCommand : FunctionAppBaseCommand
    {
        public override async Task Run()
        {
            var functionApp = await _armManager.GetFunctionApp(FunctionAppName);
            var currentTenant = await _armManager.GetCurrentTenantDomain();
            var portalHostName = "https://portal.azure.com";
            Process.Start($"{portalHostName}/{currentTenant}#resource{functionApp.ArmId}");
        }
    }
}
