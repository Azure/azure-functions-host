using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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

        [Option(0)]
        public Listable AzureResource { get; set; }

        [Option('a', "show", DefaultValue = false)]
        public bool ShowSecrets { get; set; }

        public ListSecrets(ISecretsManager secretsManager)
        {
            _secretsManager = secretsManager;
        }

        public override Task RunAsync()
        {
            try
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
                        .WriteLine()
                        .WriteLine($"{TitleColor("Tip:")} run {ExampleColor("func list <azureResource>")} to see a list of available resources from a given type.")
                        .WriteLine($"     then run {ExampleColor("func set secret <resourceName>")} to set the secret locally");
                }
            }
            catch (FileNotFoundException)
            {
                ColoredConsole
                    .Error
                    .WriteLine($"Can not find file {SecretsManager.SecretsFilePath}.")
                    .Write($"Make sure you are in the root of your functions repo, and have ran")
                    .Write($" {ExampleColor("func init")} in there.")
                    .WriteLine();
            }
            return Task.CompletedTask;
        }
    }
}
