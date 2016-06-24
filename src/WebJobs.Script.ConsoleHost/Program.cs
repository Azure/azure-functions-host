// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using CommandLine;
using WebJobs.Script.ConsoleHost.Cli;
using WebJobs.Script.ConsoleHost.Scenarios;
using WebJobs.Script.ConsoleHost.Common;
using System.Diagnostics;
using Microsoft.Azure.WebJobs.Host;

namespace WebJobs.Script.ConsoleHost
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                args = new[] { "web" };
            }

            var options = new CommandLineOptions();

            Scenario scenario = null;

            Action<string, object> setScenario = (v, o) =>
            {
                var baseOptions = o as BaseOptions;
                TraceWriter tracer = null;
                if (string.IsNullOrEmpty(baseOptions?.LogFile))
                {
                    tracer = new ConsoleTracer(TraceLevel.Info);
                }
                else
                {
                    tracer = new FileTracer(TraceLevel.Info, baseOptions.LogFile);
                }

                if (v == Verbs.Web)
                {
                    scenario = new WebScenario(o as WebVerbOptions, tracer);
                }
                else if (v == Verbs.Run)
                {
                    scenario = new RunScenario(o as RunVerbOptions, tracer);
                }
                else if (v == Verbs.Cert)
                {
                    scenario = new CertScenario(o as CertVerbOptions, tracer);
                }
                else
                {
                    Console.WriteLine($"Unknown command {args[0]}");
                    Environment.Exit(Parser.DefaultExitCodeFail);
                }
            };

            if (!Parser.Default.ParseArguments(args, options, setScenario))
            {
                Console.WriteLine("Error parsing arguments");
                Environment.Exit(Parser.DefaultExitCodeFail);
            }

            Task.Run(scenario.Run).Wait();
        }
    }
}
