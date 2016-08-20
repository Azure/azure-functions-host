// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Linq;
using System.Threading.Tasks;
using Colors.Net;
using NCli;
using WebJobs.Script.Cli.Arm;
using WebJobs.Script.Cli.Common;
using WebJobs.Script.Cli.Interfaces;
using static WebJobs.Script.Cli.Common.OutputTheme;

namespace WebJobs.Script.Cli.Verbs.List
{
    [Verb("list", Scope = Listable.FunctionApps, HelpText = "Lists function apps in current tenant. See switch-tenant command")]
    internal class ListFunctionApps : BaseListVerb
    {
        private readonly IArmManager _armManager;

        public ListFunctionApps(IArmManager armManager, ITipsManager tipsManager)
            : base(tipsManager)
        {
            _armManager = armManager;
        }

        public override async Task RunAsync()
        {
            var tenant = await _armManager.GetCurrentTenantAsync();
            ColoredConsole
                .WriteLine(VerboseColor($"Tenant: {tenant.displayName} ({tenant.domain})"))
                .WriteLine();

            var user = await _armManager.GetUserAsync();
            var functionApps = await _armManager.GetFunctionAppsAsync();
            if (functionApps.Any())
            {
                ColoredConsole.WriteLine(TitleColor("Function Apps:"));

                foreach (var app in functionApps)
                {
                    ColoredConsole
                        .WriteLine($"   -> {TitleColor("Name")}:   {app.SiteName} ({AdditionalInfoColor(app.Location)})")
                        .WriteLine($"      {TitleColor("Git Url")}: https://{user.PublishingUserName}@{app.SiteName}.scm.azurewebsites.net/")
                        .WriteLine();
                }
            }
            else
            {
                ColoredConsole.Error.WriteLine(ErrorColor("   -> No function apps found"));
            }

            _tipsManager.DisplayTip($"{TitleColor("Tip:")} to switch tenants run {ExampleColor("func switch-tenants")}");
        }
    }
}
