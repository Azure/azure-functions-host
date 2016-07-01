using CommandLine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WebJobs.Script.ConsoleHost.Common;

namespace WebJobs.Script.ConsoleHost.Scenarios
{
    public class NewScenario : Scenario
    {
        [ValueOption(0)]
        public NewOptions NewOption { get; set; }

        [Option('n', "name", HelpText = "")]
        public string FunctionAppName { get; set; }

        public override async Task Run()
        {
            if (NewOption == NewOptions.Function)
            {
                var exe = new Executable("yo", "azurefunctions", streamOutput: false, shareConsole: true);
                await exe.RunAsync();
            }
            else if (NewOption == NewOptions.FunctionApp)
            {
                FunctionAppName = FunctionAppName ?? $"functions{Path.GetRandomFileName().Replace(".", "")}";
            }
        }
    }
}
