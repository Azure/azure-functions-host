using System;
using System.Linq;
using System.Threading.Tasks;
using Colors.Net;
using Fclp;
using WebJobs.Script.Cli.Helpers;
using WebJobs.Script.Cli.Interfaces;

namespace WebJobs.Script.Cli.Actions.LocalActions
{
    [Action(Name = "add", Context = Context.Settings, HelpText = "Add new local app setting to appsettings.json")]
    class AddSettingAction : BaseAction
    {
        private readonly ISecretsManager _secretsManager;

        public string Name { get; set; }
        public string Value { get; set; }

        public AddSettingAction(ISecretsManager secretsManager)
        {
            _secretsManager = secretsManager;
        }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            if (args.Length == 0)
            {
                throw new ArgumentException("Must specify setting name.");
            }
            else
            {
                Name = args.FirstOrDefault();
                Value = args.Skip(1).FirstOrDefault();
                return base.ParseArgs(args);
            }
        }

        public override Task RunAsync()
        {
            if (string.IsNullOrEmpty(Value))
            {
                ColoredConsole.Write("Please enter the value: ");
                Value = SecurityHelpers.ReadPassword();
            }
            _secretsManager.SetSecret(Name, Value);
            return Task.CompletedTask;
        }
    }
}
