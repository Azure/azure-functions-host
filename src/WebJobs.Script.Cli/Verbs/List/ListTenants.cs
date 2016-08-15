// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using NCli;
using WebJobs.Script.Cli.Common;

namespace WebJobs.Script.Cli.Verbs.List
{
    [Verb("list", Scope = Listable.Tenants, HelpText = "Lists function apps in current tenant. See switch-tenant command")]
    internal class ListTenants : BaseListVerb
    {
        public override Task RunAsync()
        {
            throw new NotImplementedException();
        }
    }
}
