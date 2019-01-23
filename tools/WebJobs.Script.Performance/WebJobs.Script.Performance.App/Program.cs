using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommandLine;
using WebJobs.Script.Tests.EndToEnd.Shared;

namespace WebJobs.Script.PerformanceMeter
{
    class Program
    {

        public class Options
        {
            [Option('r', "runtime", Required = false, HelpText = "Private site extension url.")]
            public string RuntimeExtensionPackageUrl { get; set; }

            [Option('t', "tests", Required = false, HelpText = "List of test to execute.")]
            public IEnumerable<string> Tests { get; set; }
        }

        static async Task Main(string[] args)
        {
            // For tests
            // args = new string[] { "-t", "win-csharp-ping.jmx", "-r", "https://ci.appveyor.com/api/buildjobs/pstc3ypcff897hc4/artifacts/Functions.Private.2.0.12279-prerelease.win-x32.inproc.zip" };
            Console.Write("Args:");
            foreach (string a in args)
            {
                Console.Write(a + " ");
            }
            Console.WriteLine();

            var result = Parser.Default.ParseArguments<Options>(args);
            await result.MapResult(
                async (Options opts) => await RunAddAndReturnExitCodeAsync(opts),
                err => Task.FromResult(-1));
        }

        static async Task RunAddAndReturnExitCodeAsync(Options o)
        {
            Settings.RuntimeExtensionPackageUrl = o.RuntimeExtensionPackageUrl;
            using (PerformanceManager manager = new PerformanceManager())
            {
                if (o.Tests.ToArray().Length > 0)
                {
                    foreach (var test in o.Tests)
                    {
                        Console.WriteLine($"Executing specified test '{test}'...");
                        await manager.ExecuteAsync(test);
                    }
                }
                else
                {
                    Console.WriteLine("Executing all available tests...");
                    await manager.ExecuteAllAsync();
                }
            }
        }
    }
}
