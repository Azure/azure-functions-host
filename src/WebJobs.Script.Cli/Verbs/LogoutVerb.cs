// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Colors.Net;
using NCli;
using WebJobs.Script.Cli.Arm;

namespace WebJobs.Script.Cli.Verbs
{
    [Verb(HelpText = "Clears login cache")]
    internal class LogoutVerb : BaseVerb
    {
        private readonly IArmManager _armManager;

        public LogoutVerb(IArmManager armManager)
        {
            _armManager = armManager;
        }

        public override Task RunAsync()
        {
            _armManager.DumpTokenCache();

            ColoredConsole.WriteLine("Logged out");

            return Task.CompletedTask;
        }
    }
}
