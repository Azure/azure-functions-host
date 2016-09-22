// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Colors.Net;
using NCli;
using WebJobs.Script.Cli.Arm;
using WebJobs.Script.Cli.Helpers;
using WebJobs.Script.Cli.Interfaces;
using static WebJobs.Script.Cli.Common.OutputTheme;

namespace WebJobs.Script.Cli.Verbs
{
    [Verb(HelpText = "Helps manage publishing username", Usage = "<userName>")]
    internal class UserVerb : BaseVerb
    {
        private readonly IArmManager _armManager;

        [Option(0)]
        public string UserName { get; set; }

        public UserVerb(IArmManager armManager, ITipsManager tipsManager)
            : base(tipsManager)
        {
            _armManager = armManager;
        }

        public override async Task RunAsync()
        {
            if (string.IsNullOrEmpty(UserName))
            {
                var verb = new GitConfigVerb(_armManager, _tipsManager);
                await verb.RunAsync();
            }
            else
            {
                ColoredConsole.WriteLine($"Enter password for {AdditionalInfoColor($"\"{UserName}\":")}");
                var password = SecurityHelpers.ReadPassword();
                ColoredConsole.Write($"Confirm your password:");
                var confirmPassword = SecurityHelpers.ReadPassword();
                if (confirmPassword != password)
                {
                    ColoredConsole.Error.WriteLine(ErrorColor("passwords do not match"));
                }
                else
                {
                    await _armManager.UpdateUserAsync(UserName, password);
                    ColoredConsole
                        .WriteLine($"Password for {AdditionalInfoColor($"\"{UserName}\"")} has been updated!");
                }
            }
        }
    }
}
