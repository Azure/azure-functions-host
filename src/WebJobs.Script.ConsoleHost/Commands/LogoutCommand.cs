using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
