using CommandLine;

namespace WebJobs.Script.ConsoleHost.Commands
{
    public abstract class FunctionAppBaseCommand : BaseArmCommand
    {
        [ValueOption(0)]
        public string FunctionAppName { get; set; }
    }
}
