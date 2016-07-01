using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
