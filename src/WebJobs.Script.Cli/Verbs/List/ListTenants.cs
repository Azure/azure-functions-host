// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Linq;
using System.Threading.Tasks;
using Colors.Net;
using NCli;
using WebJobs.Script.Cli.Arm;
using WebJobs.Script.Cli.Common;

namespace WebJobs.Script.Cli.Verbs.List
{
    [Verb("list", Scope = Listable.Tenants, HelpText = "Lists function apps in current tenant. See switch-tenant command")]
    internal class ListTenants : BaseListVerb
    {
        private readonly IArmManager _armManager;

        public ListTenants(IArmManager armManager)
        {
            _armManager = armManager;
        }

        public override Task RunAsync()
        {
            _armManager.DumpTokenCache().ToList().ForEach(e => ColoredConsole.WriteLine(e));
            return Task.CompletedTask;
        }
    }
}
