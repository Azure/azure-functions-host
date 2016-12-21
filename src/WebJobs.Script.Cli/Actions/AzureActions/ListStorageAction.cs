using System;
using System.Linq;
using System.Threading.Tasks;
using Colors.Net;
using WebJobs.Script.Cli.Arm;
using WebJobs.Script.Cli.Interfaces;
using static WebJobs.Script.Cli.Common.OutputTheme;

namespace WebJobs.Script.Cli.Actions.AzureActions
{
    [Action(Name = "list", Context = Context.Azure, SubContext = Context.Storage, HelpText = "List all Storage Accounts in the selected Azure subscription")]
    class ListStorageAction : BaseAction
    {
        private readonly IArmManager _armManager;
        private readonly ISettings _settings;

        public ListStorageAction(IArmManager armManager, ISettings settings)
        {
            _armManager = armManager;
            _settings = settings;
        }

        public override async Task RunAsync()
        {
            var storageAccounts = await _armManager.GetStorageAccountsAsync(await _armManager.GetSubscriptionAsync(_settings.CurrentSubscription));
            if (storageAccounts.Any())
            {
                ColoredConsole.WriteLine(TitleColor("Storage Accounts:"));

                foreach (var storageAccount in storageAccounts)
                {
                    ColoredConsole
                        .WriteLine($"   -> {TitleColor("Name")}: {storageAccount.StorageAccountName} ({AdditionalInfoColor(storageAccount.Location)})")
                        .WriteLine();
                }
            }
            else
            {
                ColoredConsole.Error.WriteLine(ErrorColor("   -> No storage accounts found"));
            }
        }
    }
}
