using System;
using System.Threading.Tasks;

namespace WebJobs.Script.Cli.Actions.HostActions
{
    [Action(Name = "stop", Context = Context.Host)]
    class StopHostAction : BaseAction
    {
        public override Task RunAsync()
        {
            throw new NotImplementedException();
        }
    }
}
