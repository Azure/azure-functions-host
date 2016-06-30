using CommandLine;
using WebJobs.Script.ConsoleHost.Cli.Types;

namespace WebJobs.Script.ConsoleHost.Cli
{
    public class InitVerbOptions : BaseAbstractOptions
    {
        [Option("vsc", DefaultValue = SourceControl.Git, HelpText = "")]
        public SourceControl SourceControl { get; set; }
    }
}
