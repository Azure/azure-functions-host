using CommandLine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WebJobs.Script.ConsoleHost.Common;

namespace WebJobs.Script.ConsoleHost.Commands
{
    [CommandNames("st", "switch-tenants")]
    public class SwitchTenantsCommand : BaseArmCommand
    {
        [ValueOption(0)]
        public string TenantId { get; set; }

        public override async Task Run()
        {
            if (string.IsNullOrEmpty(TenantId))
            {
                _armManager.DumpTokenCache().ToList().ForEach(TraceInfo);
            }
            else
            {
                await _armManager.SelectTenant(TenantId);
            }
        }
    }
}
