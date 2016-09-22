// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Colors.Net;
using NCli;
using WebJobs.Script.Cli.Arm;
using WebJobs.Script.Cli.Arm.Models;
using WebJobs.Script.Cli.Common;
using WebJobs.Script.Cli.Interfaces;
using static WebJobs.Script.Cli.Common.OutputTheme;

namespace WebJobs.Script.Cli.Verbs
{
    [Verb(HelpText = "Handle creating a new function or function app", Usage = "[function/functionApp] <functionAppName>")]
    internal class NewVerb : BaseVerb
    {
        private readonly IArmManager _armManager;

        [Option(0)]
        public Newable NewOption { get; set; }

        [Option(1)]
        public string FunctionAppName { get; set; }

        [Option('s', "subscription", HelpText = "Subscription to create function app in")]
        public string Subscription { get; set; }

        [Option('l', "location", DefaultValue = "WestUS", HelpText = "Geographical location for your function app")]
        public string Location { get; set; }

        public NewVerb(IArmManager armManager, ITipsManager tipsManager)
            : base(tipsManager)
        {
            _armManager = armManager;
        }

        public override async Task RunAsync()
        {
            if (NewOption == Newable.Function)
            {
                var exe = new Executable("yo", "azurefunctions", streamOutput: false, shareConsole: true);
                await exe.RunAsync();
            }
            else if (NewOption == Newable.FunctionApp)
            {
                FunctionAppName = FunctionAppName ?? $"functions{Path.GetRandomFileName().Replace(".", "")}";
                var subscriptions = await _armManager.GetSubscriptionsAsync();
                if (string.IsNullOrEmpty(Subscription) && subscriptions.Count() != 1)
                {
                    ColoredConsole
                        .Error.WriteLine(ErrorColor("Can't determin subscription Id, please add -s/--subscription <SubId>"))
                        .WriteLine()
                        .WriteLine(TitleColor("Subscriptions in current tenant:"));

                    var longestDisplayName = subscriptions.Max(s => s.DisplayName.Length);

                    foreach (var subscription in subscriptions)
                    {
                        ColoredConsole.WriteLine(string.Format($"    {{0, {-longestDisplayName}}} ({{1}})", subscription.DisplayName, subscription.SubscriptionId));
                    }

                    ColoredConsole
                        .WriteLine()
                        .WriteLine($"To switch tenants run {ExampleColor("'func switch-tenants'")}");
                }
                else
                {
                    var subscription = string.IsNullOrEmpty(Subscription) ? subscriptions.First() : new Subscription(Subscription, string.Empty);
                    var functionApp = await _armManager.CreateFunctionAppAsync(subscription, FunctionAppName, Location);
                    ColoredConsole.WriteLine($"Function app {AdditionalInfoColor($"\"{functionApp.SiteName}\"")} has been created");
                }
            }
        }
    }
}
