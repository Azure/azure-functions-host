// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Colors.Net;
using NCli;
using static WebJobs.Script.Cli.Common.OutputTheme;

namespace WebJobs.Script.Cli.Verbs
{
    internal abstract class BaseVerb : IVerb, IVerbError, IVerbPostRun
    {
        [Option("quiet", DefaultValue = false, HelpText = "Disable all logging", ShowInHelp = false)]
        public bool Quiet { get; set; }

        [Option("cli-dev", DefaultValue = false, HelpText = "Display exceptions for reporting issues", ShowInHelp = false)]
        public bool CliDev { get; set; }

        public string OriginalVerb { get; set; }

        public IDependencyResolver DependencyResolver { get; set; }

        public abstract Task RunAsync();

        public Task OnErrorAsync(Exception e)
        {
            if (CliDev)
            {
                ColoredConsole
                    .Error
                    .WriteLine(ErrorColor(e.ToString()));
            }
            else
            {
                ColoredConsole
                    .Error
                    .WriteLine(ErrorColor($"Error: {e.Message}"));

                ColoredConsole
                    .WriteLine($"You can run the same command passing {ExampleColor("--cli-dev")} and report an issue on https://github.com/azure/azure-webjobs-sdk-script/issues");
            }
            return Task.CompletedTask;
        }

        public Task PostRunVerbAsync()
        {
            // TipsManager.GetTipsFor(this.GetType());
            return Task.CompletedTask;
        }
    }
}
