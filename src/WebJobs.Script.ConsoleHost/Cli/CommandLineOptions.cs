// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using CommandLine;

namespace WebJobs.Script.ConsoleHost.Cli
{
    public class CommandLineOptions
    {
        [VerbOption(Verbs.Web, HelpText = "Web server options")]
        public WebVerbOptions WebOptions { get; set; }

        [VerbOption(Verbs.Run, HelpText = "Run stuff")]
        public RunVerbOptions RunOptions { get; set; }

        [VerbOption(Verbs.Cert, HelpText = "Cert stuff")]
        public CertVerbOptions CertOptions { get; set; }
    }
}
