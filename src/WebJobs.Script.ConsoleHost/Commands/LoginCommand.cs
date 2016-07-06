// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Linq;
using System.Threading.Tasks;

namespace WebJobs.Script.ConsoleHost.Commands
{
    public class LoginCommand : BaseArmCommand
    {
        public override async Task Run()
        {
            await _armManager.Login();
            _armManager.DumpTokenCache().ToList().ForEach(TraceInfo);
        }
    }
}
