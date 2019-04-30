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
        static async Task Main(string[] args)
        {
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
            Settings.RuntimeExtensionPackageUrl = o.RuntimeUrl;
            using (PerformanceManager manager = new PerformanceManager())
            {
                Console.WriteLine($"Executing specified test '{o.RunFromZip}'...");
                await manager.ExecuteAsync(o);
            }
        }
    }
    public class Options
    {

        [Option('u', "runtimeUrl", Required = true, HelpText = "Runtime url")]
        public string RuntimeUrl { get; set; }

        [Option('r', "runtime", Required = true, HelpText = "Runtime")]
        public string Runtime { get; set; }

        [Option('z', "runfromzip", Required = true, HelpText = "run-from-zip url")]
        public string RunFromZip { get; set; }

        [Option('j', "jmx", Required = true, HelpText = "Jmx url")]
        public string Jmx { get; set; }

        [Option('d', "description", Required = true, HelpText = "Description")]
        public string Description { get; set; }
    }
}
