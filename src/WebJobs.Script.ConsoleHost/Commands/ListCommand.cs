// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;

namespace WebJobs.Script.ConsoleHost.Commands
{
    public class ListCommand : BaseArmCommand
    {
        public override async Task Run()
        {
            foreach (var app in await _armManager.GetFunctionApps())
            {
                TraceInfo($"{app.SiteName} ({app.Location})");
            }
        }
    }
}
