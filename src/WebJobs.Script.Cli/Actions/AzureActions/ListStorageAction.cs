using System;
using System.Linq;
using System.Threading.Tasks;
using Colors.Net;
using WebJobs.Script.Cli.Arm;
using static WebJobs.Script.Cli.Common.OutputTheme;

namespace WebJobs.Script.Cli.Actions.AzureActions
{
    [Action(Name = "list", Context = Context.Azure, SubContext = Context.Storage)]
    class ListStorageAction : BaseAction
    {
        private readonly IArmManager _armManager;

        public ListStorageAction(IArmManager armManager)
        {
            _armManager = armManager;
        }

        public override async Task RunAsync()
        {
            var storageAccounts = await _armManager.GetStorageAccountsAsync();
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
