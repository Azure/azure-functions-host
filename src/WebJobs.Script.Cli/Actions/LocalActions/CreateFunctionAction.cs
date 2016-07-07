using System;
using System.Threading.Tasks;
using WebJobs.Script.Cli.Common;

namespace WebJobs.Script.Cli.Actions.LocalActions
{
    [Action(Name = "create", Context = Context.Function)]
    class CreateFunctionAction : BaseAction
    {
        public override async Task RunAsync()
        {
            var exe = new Executable("yo", "azurefunctions", streamOutput: false, shareConsole: true);
            await exe.RunAsync();
        }
    }
}
