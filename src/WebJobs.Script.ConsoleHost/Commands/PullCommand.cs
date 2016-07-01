using System;
using System.Linq;
using System.Threading.Tasks;

namespace WebJobs.Script.ConsoleHost.Commands
{
    public class PullCommand : FunctionAppBaseCommand
    {
        public override async Task Run()
        {
            var user = await _armManager.GetUser();
            if (user?.publishingUserName == null)
            {
                TraceInfo("Set your user");
            }
            else
            {
                var functionApps = await _armManager.GetFunctionApps();
                var functionApp = functionApps.FirstOrDefault(s => s.SiteName.Equals(FunctionAppName, StringComparison.OrdinalIgnoreCase));
                if (functionApp != null)
                {
                    functionApp = await _armManager.Load(functionApp);
                    TraceInfo($"Run: `git clone {functionApp.ScmHostName}/{functionApp.SiteName}.git`");
                    TraceInfo($"UserName: {user.publishingUserName}");
                }
                else
                {
                    TraceInfo($"Can't Find Function App Named {FunctionAppName} in tenant");
                }
            }
        }
    }
}
