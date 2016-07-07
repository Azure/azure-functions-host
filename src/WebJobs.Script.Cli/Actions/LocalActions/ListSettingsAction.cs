using System;
using System.Threading.Tasks;
using Colors.Net;
using Fclp;
using WebJobs.Script.Cli.Interfaces;
using static WebJobs.Script.Cli.Common.OutputTheme;

namespace WebJobs.Script.Cli.Actions.LocalActions
{
    [Action(Name = "list", Context = Context.Settings)]
    class ListSettingsAction : BaseAction
    {
        private readonly ISecretsManager _secretsManager;
        public bool ShowValues { get; set; }

        public ListSettingsAction(ISecretsManager secretsManager)
        {
            _secretsManager = secretsManager;
        }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<bool>('a', "showValue")
                .Callback(a => ShowValues = a);
            return Parser.Parse(args);
        }

        public override Task RunAsync()
        {
            foreach (var pair in _secretsManager.GetSecrets())
            {
                ColoredConsole
                    .WriteLine($"   -> {TitleColor("Name")}: {pair.Key}")
                    .WriteLine($"      {TitleColor("Value")}: {(ShowValues ? pair.Value : "*****")}")
                    .WriteLine();
            }
            return Task.CompletedTask;
        }
    }
}
