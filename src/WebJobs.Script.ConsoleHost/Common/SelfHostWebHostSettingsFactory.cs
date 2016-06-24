using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.WebHost;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WebJobs.Script.ConsoleHost.Common
{
    public static class SelfHostWebHostSettingsFactory
    {
        public static WebHostSettings Create(TraceWriter traceWriter = null)
        {
            return new WebHostSettings
            {
                IsSelfHost = true,
                ScriptPath = Path.Combine(Environment.CurrentDirectory),
                LogPath = Path.Combine(Path.GetTempPath(), @"LogFiles\Application\Functions"),
                SecretsPath = Path.Combine(Environment.CurrentDirectory, "data", "functions", "secrets"),
                TraceWriter = traceWriter
            };
        }
    }
}
