// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using CommandLine;
using CommandLine.Text;

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
