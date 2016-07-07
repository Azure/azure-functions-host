using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WebJobs.Script.Cli.Interfaces;

namespace WebJobs.Script.Cli.Actions.LocalActions
{
    [Action(Name = "encrypt", Context = Context.Settings)]
    class EncryptSettingsAction : BaseAction
    {
        private readonly ISecretsManager _secretsManager;

        public EncryptSettingsAction(ISecretsManager secretsManager)
        {
            _secretsManager = secretsManager;
        }

        public override Task RunAsync()
        {
            _secretsManager.EncryptSettings();
            return Task.CompletedTask;
        }
    }
}
