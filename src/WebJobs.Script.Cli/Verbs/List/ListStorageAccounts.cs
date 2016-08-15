// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Linq;
using System.Threading.Tasks;
using Colors.Net;
using NCli;
using WebJobs.Script.Cli.Arm;
using WebJobs.Script.Cli.Common;
using static WebJobs.Script.Cli.Common.OutputTheme;

namespace WebJobs.Script.Cli.Verbs.List
{
    [Verb("list", Scope = Listable.StorageAccounts, HelpText = "Lists function apps in current tenant. See switch-tenant command")]
    internal class ListStorageAccounts : BaseListVerb
    {
        private readonly IArmManager _armManager;

        public ListStorageAccounts(IArmManager armManager)
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
