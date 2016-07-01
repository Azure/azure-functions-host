using CommandLine;
using CommandLine.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WebJobs.Script.ConsoleHost.Commands
{
    public class CommandLineOptions
    {
        [VerbOption("web", HelpText = "Web stuff")]
        public WebCommand Web { get; set; }
        public CertCommand Cert { get; set; }
        public GitConfigCommand GitConfig { get; set; }


        [HelpVerbOption]
        public string GetUsage(string verb)
        {
            return HelpText.AutoBuild(this, verb).ToString();
        }
    }
}
