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
            Scenario scenario = null;
            if (!TryGetScenario(args, out scenario))
            {
                Console.WriteLine("Error parsing arguments");
                //Display help
                Environment.Exit(Parser.DefaultExitCodeFail);
            }
            else
            {
                Task.Run(scenario.Run).Wait();
            }
        }

        private static bool TryGetScenario(string[] args, out Scenario scenario)
        {
            SetDefaultArgs(ref args);

            Scenario _scenario = null;
            var options = CommandLineOptionsBuilder.CreateObject();

            Action<string, object> setScenario = (v, o) =>
            {

                foreach (var verb in CommandLineOptionsBuilder.Verbs)
                {
                    if (v.Equals(verb.Item1, StringComparison.OrdinalIgnoreCase))
                    {
                        _scenario = CommandLineOptionsBuilder.BuildScenario(verb.Item2, o);
                        return;
                    }
                }
            };

            if (!Parser.Default.ParseArguments(args, options, setScenario))
            {
                scenario = null;
                return false;
            }
            else
            {
                scenario = _scenario;
                return true;
            }
        }

        private static void SetDefaultArgs(ref string[] args)
        {
            if (args.Length == 0)
            {
                args = new[] { "web" };
            }
        }
    }
}
