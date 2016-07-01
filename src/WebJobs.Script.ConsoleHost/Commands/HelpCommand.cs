using CommandLine.Text;
using System.Threading.Tasks;
using WebJobs.Script.ConsoleHost.Common;

namespace WebJobs.Script.ConsoleHost.Commands
{
    [IgnoreCommand]
    class HelpCommand : Command
    {
        private readonly object _options;

        public HelpCommand(object options)
        {
            _options = options;
        }

        public override Task Run()
        {
            TraceInfo(HelpText.AutoBuild(_options, null));
            return Task.CompletedTask;
        }
    }
}
