using System;
using System.Threading.Tasks;
using WebJobs.Script.ConsoleHost.Common;

namespace WebJobs.Script.ConsoleHost.Commands
{
    [CommandNames("gs", "get-settings")]
    public class GetSettingsCommand : FunctionAppBaseCommand
    {
        public override Task Run()
        {
            throw new Exception();
        }
    }
}
