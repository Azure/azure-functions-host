// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Colors.Net;
using NCli;
using WebJobs.Script.Cli.Arm;
using WebJobs.Script.Cli.Common;
using WebJobs.Script.Cli.Helpers;
using WebJobs.Script.Cli.Interfaces;
using static WebJobs.Script.Cli.Common.OutputTheme;

namespace WebJobs.Script.Cli.Verbs
{
    [Verb(HelpText = "Not yet Implemented")]
    internal class SetVerb : BaseVerb
    {
        [Option(0)]
        public string ResourceName { get; set; }

        [Option('t', "type")]
        public Listable? Type { get; set; }

        [Option('n', "name")]
        public string Name { get; set; }

        [Option('v', "value")]
        public string Value { get; set; }

        private readonly IArmManager _armManager;
        private readonly ISecretsManager _secretsManager;

        private readonly Dictionary<string, Listable> armToResourceMap = new Dictionary<string, Listable>(StringComparer.OrdinalIgnoreCase)
        {
            { "Microsoft.Web/sites", Listable.FunctionApps },
            { "Microsoft.Storage/storageAccounts", Listable.StorageAccounts }
        };

        public SetVerb(IArmManager armManager, ISecretsManager secretsManager, ITipsManager tipsManager)
            : base(tipsManager)
        {
            _armManager = armManager;
            _secretsManager = secretsManager;
        }

        public override async Task RunAsync()
        {
            if (string.IsNullOrEmpty(ResourceName) && string.IsNullOrEmpty(Name))
            {
                ColoredConsole.Error.WriteLine("Please specify a resource name");
                _tipsManager.DisplayTip($"{TitleColor("Tip:")} run {ExampleColor("func help setsecrets")} for helps.");
                return;
            }
            else if (!string.IsNullOrEmpty(Name))
            {
                if(string.IsNullOrEmpty(Value))
                {
                    System.Console.Write("Enter value:");
                    Value = SecurityHelpers.ReadPassword();
                }

                _secretsManager.SetSecret(Name, Value);
                ColoredConsole
                    .WriteLine($"Secret saved locally in {ExampleColor(Name)}")
                    .WriteLine();
            }
            else if (!string.IsNullOrEmpty(ResourceName))
            {
                await LoadResourceSecretByNameAsync();
            }
        }

        private async Task LoadResourceSecretByNameAsync()
        {
            var resources = await _armManager.getAzureResourceAsync(ResourceName);

            if (!resources.Any())
            {
                ColoredConsole
                    .Error
                    .WriteLine(ErrorColor($"Can't find resource with name {ResourceName}"))
                    .WriteLine(ErrorColor("Make sure you are on the right tenant"));
            }
            else if (resources.Count() > 1 && Type == null)
            {
                ColoredConsole
                    .WriteLine($"Found {resources.Count()} resources with the same name.")
                    .Write("Please specify the resource type using")
                    .WriteLine(ExampleColor($"func set secret {ResourceName} -type <storageAccount\\eventHub\\functionApp>"));
            }
            else if (resources.Count() == 1 || resources.Count() > 1 && Type != null)
            {
                var typeString = armToResourceMap.FirstOrDefault(p => p.Value == Type).Key;
                switch (armToResourceMap[typeString ?? resources.First().Type])
                {
                    case Listable.StorageAccounts:
                        var storageAccount = await _armManager.GetStorageAccountsAsync(resources.First());
                        if (storageAccount != null)
                        {
                            _secretsManager.SetSecret($"{storageAccount.StorageAccountName}_STORAGE", storageAccount.GetConnectionString());
                            ColoredConsole
                                .WriteLine($"Secret saved locally in {ExampleColor($"{storageAccount.StorageAccountName}_STORAGE")}")
                                .WriteLine();

                            _tipsManager.DisplayTip($"{TitleColor("Tip:")} You can use that identifier {ExampleColor($"{storageAccount.StorageAccountName}_STORAGE ")} in the connection property in your function.json");
                        }
                        else
                        {
                            ColoredConsole.Error.WriteLine(ErrorColor("Can not load storage account."));
                        }
                        break;
                    default:
                        ColoredConsole.WriteLine(AdditionalInfoColor("Only storage accounts are supported currently."));
                        break;
                }
            }
        }
    }
}
