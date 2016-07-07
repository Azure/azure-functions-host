// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics;
using System.Threading.Tasks;
using NCli;
using WebJobs.Script.Cli.Arm;

namespace WebJobs.Script.Cli.Verbs
{
    [Verb(HelpText = "Launch default browser with link to the function app in https://portal.azure.com" )]
    internal class OpenVerb : BaseVerb
    {
        [Option(0)]
        public string FunctionAppName { get; set; }

        private readonly IArmManager _armManager;

        public OpenVerb(IArmManager armManager)
        {
            _armManager = armManager;
        }

        public override async Task RunAsync()
        {
            var functionApp = await _armManager.GetFunctionAppAsync(FunctionAppName);
            var currentTenant = await _armManager.GetCurrentTenantAsync();
            var portalHostName = "https://portal.azure.com";
            Process.Start($"{portalHostName}/{currentTenant.domain}#resource{functionApp.ArmId}");
        }
    }
}
