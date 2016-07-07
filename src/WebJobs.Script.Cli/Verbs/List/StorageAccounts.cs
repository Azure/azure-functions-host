using System.Linq;
using System.Threading.Tasks;
using Colors.Net;
using WebJobs.Script.Cli.Arm;
using static WebJobs.Script.Cli.Common.OutputTheme;

namespace WebJobs.Script.Cli.Verbs.List
{
    internal class StorageAccounts : BaseListVerb
    {
        private readonly IArmManager _armManager;

        public StorageAccounts(IArmManager armManager)
        {
            _armManager = armManager;
        }

        public override async Task RunAsync()
        {
            var tenant = await _armManager.GetCurrentTenantAsync();
            ColoredConsole
                .WriteLine(VerboseColor($"Tenant: {tenant.displayName} ({tenant.domain})"))
                .WriteLine();

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

            ColoredConsole
                .WriteLine()
                .WriteLine($"{TitleColor("Tip:")} to switch tenants run {ExampleColor("func switch-tenants")}");
        }
    }
}
