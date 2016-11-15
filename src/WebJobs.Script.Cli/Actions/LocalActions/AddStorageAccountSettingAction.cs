using System;
using System.Linq;
using System.Threading.Tasks;
using Colors.Net;
using Fclp;
using WebJobs.Script.Cli.Arm;
using WebJobs.Script.Cli.Interfaces;
using static WebJobs.Script.Cli.Common.OutputTheme;

namespace WebJobs.Script.Cli.Actions.LocalActions
{
    [Action(Name = "add-storage-account", Context = Context.Settings, HelpText = "Add a local app setting using the value from an Azure Storage account. Requires Azure login.")]
    class AddStorageAccountSettingAction : BaseAction
    {
        private readonly IArmManager _armManager;
        private readonly ISettings _settings;
        private readonly ISecretsManager _secretsManager;

        public string StorageAccountName { get; set; }

        public AddStorageAccountSettingAction(IArmManager armManager, ISettings settings, ISecretsManager secretsManager)
        {
            _armManager = armManager;
            _settings = settings;
            _secretsManager = secretsManager;
        }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            if (args.Any())
            {
                StorageAccountName = args.First();
            }
            else
            {
                throw new ArgumentException("Must specify storage account name.");
            }

            return base.ParseArgs(args);
        }

        public override async Task RunAsync()
        {
            var storageAccounts = await _armManager.GetStorageAccountsAsync();
            var storageAccount = storageAccounts.FirstOrDefault(st => st.StorageAccountName.Equals(StorageAccountName, StringComparison.OrdinalIgnoreCase));

            if (storageAccount == null)
            {
                ColoredConsole
                    .Error
                    .WriteLine(ErrorColor($"Can't find storage account with name {StorageAccountName} in current subscription ({_settings.CurrentSubscription})"));
            }
            else
            {
                var name = $"{storageAccount.StorageAccountName}_STORAGE";
                _secretsManager.SetSecret(name, storageAccount.GetConnectionString());
                ColoredConsole
                    .WriteLine($"Secret saved locally in {ExampleColor(name)}")
                    .WriteLine();
            }
        }
    }
}
