using ARMClient.Library;
using CommandLine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using WebJobs.Script.ConsoleHost.Common;

namespace WebJobs.Script.ConsoleHost.Commands
{
    public class NewCommand : BaseArmCommand
    {
        [ValueOption(0)]
        public Functish NewOption { get; set; }

        [ValueOption(1)]
        public string FunctionAppName { get; set; }

        [Option('s', "subscription", HelpText = "")]
        public string Subscription { get; set; }

        public override async Task Run()
        {
            if (NewOption == Functish.Function)
            {
                var exe = new Executable("yo", "azurefunctions", streamOutput: false, shareConsole: true);
                await exe.RunAsync();
            }
            else if (NewOption == Functish.FunctionApp)
            {
                FunctionAppName = FunctionAppName ?? $"functions{Path.GetRandomFileName().Replace(".", "")}";
                var subscriptions = await _armManager.GetSubscriptions();
                if (subscriptions.Count() != 1)
                {
                    TraceInfo("Can't determin subscription Id, please add -s/--subscription <SubId>");
                }
                else
                {
                    await _armManager.CreateFunctionApp(subscriptions.First(), FunctionAppName, GeoLocation.WestUS);
                }
            }
        }
    }
}
