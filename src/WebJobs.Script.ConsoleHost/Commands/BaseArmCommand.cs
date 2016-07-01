using CommandLine;

namespace WebJobs.Script.ConsoleHost.Commands
{
    public abstract class BaseArmCommand : Command
    {
        [ValueOption(0)]
        public string FunctionAppName { get; set; }
    }
}
