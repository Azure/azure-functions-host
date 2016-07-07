using System;
using System.Threading.Tasks;

namespace WebJobs.Script.Cli.Actions.LocalActions
{
    [Action(Name = "add-storage-account", Context = Context.Settings)]
    class AddStorageAccountSettingAction : BaseAction
    {
        public override Task RunAsync()
        {
            throw new NotImplementedException();
        }
    }
}
