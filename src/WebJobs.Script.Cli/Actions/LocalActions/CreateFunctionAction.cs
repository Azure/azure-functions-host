using System;
using System.Threading.Tasks;
using WebJobs.Script.Cli.Common;

namespace WebJobs.Script.Cli.Actions.LocalActions
{
    [Action(Name = "create", Context = Context.Function, HelpText = "Create a new Function from a template, using the Yeoman generator")]
    class CreateFunctionAction : BaseAction
    {
        public override async Task RunAsync()
        {
            var exe = new Executable("yo", "azurefunctions", streamOutput: false, shareConsole: true);
            await exe.RunAsync();
        }
    }
}
