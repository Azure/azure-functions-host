using ARMClient.Library;
using CommandLine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using WebJobs.Script.ConsoleHost.Arm;
using WebJobs.Script.ConsoleHost.Common;

namespace WebJobs.Script.ConsoleHost.Commands
{
    public class ListCommand : BaseArmCommand
    {
        [ValueOption(0)]
        public NewOptions ListOption { get; set; }

        public override async Task Run()
        {
            if (ListOption == NewOptions.FunctionApp)
            {
                var armManager = new ArmManager();
                var functionApps = await armManager.GetFunctionApps();
                foreach (var app in functionApps)
                {
                    TraceInfo(app.SiteName);
                }
            }
            else
            {
                TraceInfo("not supported");
            }
        }
    }
}
