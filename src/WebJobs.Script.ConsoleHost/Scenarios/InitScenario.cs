using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WebJobs.Script.ConsoleHost.Cli;
using WebJobs.Script.ConsoleHost.Cli.Types;
using WebJobs.Script.ConsoleHost.Common;

namespace WebJobs.Script.ConsoleHost.Scenarios
{
    public class InitScenario : Scenario
    {
        private InitVerbOptions _options;

        private readonly Dictionary<string, string> fileToContentMap = new Dictionary<string, string>
        {
            { ".gitignore",  @"
bin
obj
csx
.vs
edge
Publish

*.user
*.suo
*.cscfg
*.Cache

/packages
/TestResults

/tools/NuGet.exe
/App_Data
/secrets
/data
"},
            { ScriptConstants.HostMetadataFileName, "{}" }
        };


        public InitScenario(InitVerbOptions options, TraceWriter tracer) : base (tracer)
        {
            _options = options;
        }

        public override async Task Run()
        {
            if (_options.SourceControl != SourceControl.Git)
            {
                throw new Exception("Only Git is supported right now for vsc");
            }

            foreach (var pair in fileToContentMap)
            {
                TraceInfo($"Writing {pair.Key}");
                using (var writer = new StreamWriter(pair.Key))
                {
                    await writer.WriteAsync(pair.Value);
                }
            }

            var exe = new Executable("git", "init");
            await exe.RunAsync(TraceInfo, TraceInfo);
        }
    }
}
