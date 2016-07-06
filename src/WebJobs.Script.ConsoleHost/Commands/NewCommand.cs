// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommandLine;
using WebJobs.Script.ConsoleHost.Arm.Models;
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

        [Option('l', "location", DefaultValue = GeoLocation.WestUS, HelpText = "")]
        public GeoLocation Location { get; set; }

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
                if (string.IsNullOrEmpty(Subscription) && subscriptions.Count() != 1)
                {
                    TraceInfo("Can't determin subscription Id, please add -s/--subscription <SubId>");
                }
                else
                {
                    var subscription = string.IsNullOrEmpty(Subscription) ? subscriptions.First() : new Subscription(Subscription, string.Empty);
                    var functionApp = await _armManager.CreateFunctionApp(subscription, FunctionAppName, Location);
                    TraceInfo($"Function App \"{functionApp.SiteName}\" has been created");
                }
            }
        }
    }
}
