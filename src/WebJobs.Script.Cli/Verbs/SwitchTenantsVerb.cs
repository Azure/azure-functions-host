// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Linq;
using System.Threading.Tasks;
using Colors.Net;
using NCli;
using WebJobs.Script.Cli.Arm;

namespace WebJobs.Script.Cli.Verbs
{
    [Verb("switch-tenants", HelpText = "List and switch current tenant for the Cli", Usage = "<tenantId>")]
    internal class SwitchTenantsVerb : BaseVerb
    {
        private readonly IArmManager _armManager;

        [Option(0)]
        public string TenantId { get; set; }

        public SwitchTenantsVerb(IArmManager armManager)
        {
            _armManager = armManager;
        }

        public override async Task RunAsync()
        {
            if (string.IsNullOrEmpty(TenantId))
            {
                _armManager.DumpTokenCache().ToList().ForEach(e => ColoredConsole.WriteLine(e));
            }
            else
            {
                await _armManager.SelectTenantAsync(TenantId);
            }
        }
    }
}
