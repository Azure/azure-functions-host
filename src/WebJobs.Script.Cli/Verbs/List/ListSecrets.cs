// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Linq;
using System.Threading.Tasks;
using Colors.Net;
using NCli;
using WebJobs.Script.Cli.Common;
using WebJobs.Script.Cli.Interfaces;
using static WebJobs.Script.Cli.Common.OutputTheme;

namespace WebJobs.Script.Cli.Verbs.List
{
    [Verb("list", Scope = Listable.Secrets, HelpText = "Lists function apps in current tenant. See switch-tenant command")]
    internal class ListSecrets : BaseListVerb
    {
        private readonly ISecretsManager _secretsManager;

        [Option('a', "show", DefaultValue = false, HelpText = "Display the secret value")]
        public bool ShowSecrets { get; set; }

        public ListSecrets(ISecretsManager secretsManager, ITipsManager tipsManager)
            : base(tipsManager)
        {
            _secretsManager = secretsManager;
        }

        public override Task RunAsync()
        {
            var secrets = _secretsManager.GetSecrets();
            if (secrets.Any())
            {
                ColoredConsole.WriteLine(TitleColor("Locally configured secrets:"));
                foreach (var pair in secrets)
                {
                    ColoredConsole
                        .WriteLine($"   -> {TitleColor("Name")}: {pair.Key}")
                        .WriteLine($"      {TitleColor("Value")}: {(ShowSecrets ? pair.Value : "*****")}")
                        .WriteLine();
                }
            }
            else
            {
                ColoredConsole
                    .WriteLine("No secrets currently configured locally.")
                    .WriteLine();

                _tipsManager
                    .DisplayTips(
                        $"{TitleColor("Tip:")} run {ExampleColor("func list <azureResource>")} to see a list of available resources from a given type.",
                        $"     then run {ExampleColor("func set secret <resourceName>")} to set the secret locally");
            }
            return Task.CompletedTask;
        }
    }
}
