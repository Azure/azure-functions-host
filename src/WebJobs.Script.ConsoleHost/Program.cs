// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using CommandLine;
using WebJobs.Script.ConsoleHost.Commands;
using System.Linq;

namespace WebJobs.Script.ConsoleHost
{
    class Program
    {
        static void Main(string[] args)
        {
            Command scenario = null;
            if (!TryGetScenario(args, out scenario))
            {
                Console.WriteLine("Error parsing arguments");
                Environment.Exit(Parser.DefaultExitCodeFail);
            }
            else
            {
                Task.Run(scenario.Run).Wait();
            }
        }

        private static bool TryGetScenario(string[] args, out Command command)
        {
            SetDefaultArgs(ref args);

            Command _command = null;
            var options = CommandLineOptionsBuilder.CreateObject();

            if (!Parser.Default.ParseArguments(args, options, (v, c) => _command = c as Command) ||
                args[0].Equals("help", StringComparison.OrdinalIgnoreCase))
            {
                command = new HelpCommand(options);
                return true;
            }
            else
            {
                command = _command;
                command.OriginalCommand = args[0];
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
