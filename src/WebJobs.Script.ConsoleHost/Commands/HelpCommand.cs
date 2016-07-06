// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using CommandLine.Text;
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
