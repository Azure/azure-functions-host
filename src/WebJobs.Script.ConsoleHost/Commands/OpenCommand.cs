// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics;
using System.Threading.Tasks;

namespace WebJobs.Script.ConsoleHost.Commands
{
    public class OpenCommand : FunctionAppBaseCommand
    {
        public override async Task Run()
        {
            var functionApp = await _armManager.GetFunctionApp(FunctionAppName);
            var currentTenant = await _armManager.GetCurrentTenantDomain();
            var portalHostName = "https://portal.azure.com";
            Process.Start($"{portalHostName}/{currentTenant}#resource{functionApp.ArmId}");
        }
    }
}
