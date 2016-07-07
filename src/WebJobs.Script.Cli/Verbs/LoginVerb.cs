// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Linq;
using System.Threading.Tasks;
using Colors.Net;
using NCli;
using WebJobs.Script.Cli.Arm;

namespace WebJobs.Script.Cli.Verbs
{
    [Verb(HelpText = "Clears login cache and prompts for a new login")]
    internal class LoginVerb : BaseVerb
    {
        private readonly IArmManager _armManager;

        public LoginVerb(IArmManager armManager)
        {
            _armManager = armManager;
        }

        public override async Task RunAsync()
        {
            await _armManager.LoginAsync();
            _armManager.DumpTokenCache().ToList().ForEach(l => ColoredConsole.WriteLine(l));
        }
    }
}
