using System;
using System.Linq;
using System.Threading.Tasks;
using Fclp;
using WebJobs.Script.Cli.Interfaces;

namespace WebJobs.Script.Cli.Actions.LocalActions
{
    [Action(Name = "delete", Context = Context.Settings)]
    [Action(Name = "remove", Context = Context.Settings)]
    class DeleteSettingAction : BaseAction
    {
        private readonly ISecretsManager _secretsManager;
        public string Name { get; set; }

        public DeleteSettingAction(ISecretsManager secretsManager)
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
                Name = args.First();
                return base.ParseArgs(args);
            }
        }

        public override Task RunAsync()
        {
            _secretsManager.DeleteSecret(Name);
            return Task.CompletedTask;
        }
    }
}
