// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Colors.Net;
using NCli;
using WebJobs.Script.Cli.Arm;
using WebJobs.Script.Cli.Interfaces;
using static WebJobs.Script.Cli.Common.OutputTheme;

namespace WebJobs.Script.Cli.Verbs
{
    [Verb("git", HelpText = "Not yet Implemented", ShowInHelp = false)]
    internal class GitConfigVerb : BaseVerb
    {
        private readonly IArmManager _armManager;

        public GitConfigVerb(IArmManager armManager, ITipsManager tipsManager)
            : base(tipsManager)
        {
            _armManager = armManager;
        }

        public override async Task RunAsync()
        {
            var user = await _armManager.GetUserAsync();
            if (string.IsNullOrEmpty(user.PublishingUserName))
            {
                ColoredConsole.WriteLine($"Publishing user is not configured. Run {ExampleColor("func user <userName>")} to configure your publishing user");
            }
            else
            {
                ColoredConsole
                    .Write(TitleColor("Publishing Username: "))
                    .Write(user.PublishingUserName)
                    .WriteLine()
                    .WriteLine()
                    .Write("run ")
                    .Write(ExampleColor($"\"func user {user.PublishingUserName}\" "))
                    .WriteLine("to update the password");
            }
        }
    }
}
