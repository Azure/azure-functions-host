using System;
using System.Linq;
using System.Threading.Tasks;
using Colors.Net;
using WebJobs.Script.Cli.Arm;
using WebJobs.Script.Cli.Interfaces;
using static WebJobs.Script.Cli.Common.OutputTheme;

namespace WebJobs.Script.Cli.Actions.AzureActions
{
    [Action(Name = "list", Context = Context.Azure, SubContext = Context.FunctionApp, HelpText = "List all Function Apps in the selected Azure subscription")]
    class ListFunctionAppsAction : BaseAction
    {
        private readonly IArmManager _armManager;
        private readonly ISettings _settings;

        public ListFunctionAppsAction(IArmManager armManager, ISettings settings)
        {
            _armManager = armManager;
            _settings = settings;
        }

        public override async Task RunAsync()
        {
            var user = await _armManager.GetUserAsync();
            var functionApps = await _armManager.GetFunctionAppsAsync(await _armManager.GetSubscriptionAsync(_settings.CurrentSubscription));
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
        }
    }
}
