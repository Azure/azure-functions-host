// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;

namespace WebJobs.Script.ConsoleHost.Commands
{
    public class LogoutCommand : BaseArmCommand
    {
        public override Task Run()
        {
            _armManager.DumpTokenCache();
            TraceInfo("Logged out");
            return Task.CompletedTask;
        }
    }
}
