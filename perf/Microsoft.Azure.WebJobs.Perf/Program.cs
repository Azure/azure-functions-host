// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Perf
{
    public class Program
    {
        private const string StorageConnectionStringArgKey = "StorageConnectionString";
        private const string ScenarioStringArgKey = "Scenario";

        public static int Main(string[] args)
        {
            try
            {
                IDictionary<string, string> commandLineArgs = ParseCommandLineArgs(args);
                ValidateCommandLineArguments(commandLineArgs);

                string scenarioName = commandLineArgs[ScenarioStringArgKey];
                string storageConnectionString = commandLineArgs[StorageConnectionStringArgKey];

                switch(scenarioName)
                {
                    case "FunctionChaining":
                        FunctionChainingPerfTest.Run(storageConnectionString);
                        break;
                    case "BlobOverhead-NoLogging":
                        BlobOverheadPerfTest.Run(storageConnectionString, disableLogging: true);
                        break;
                    case "BlobOverhead-Logging":
                        BlobOverheadPerfTest.Run(storageConnectionString, disableLogging: false);
                        break;
                    case "QueueOverhead-NoLogging":
                        QueueOverheadPerfTest.Run(storageConnectionString, disableLogging: true);
                        break;
                    case "QueueOverhead-Logging":
                        QueueOverheadPerfTest.Run(storageConnectionString, disableLogging: false);
                        break;
                    default:
                        throw new ArgumentException("Invalid scenario");
                }
            }
            catch(Exception ex)
            {
                Console.WriteLine("Usage:");
                Console.WriteLine("Microsoft.Azure.WebJobs.Perf.exe /Scenario=<scenario name> /StorageConnectionString=<storage connection string>");
                Console.WriteLine();
                Console.WriteLine("--- Exception details ---");
                Console.WriteLine(ex.ToString());

                return -1;
            }

            return 0;
        }

        private static void ValidateCommandLineArguments(IDictionary<string, string> args)
        {
            foreach(var arg in args)
            {
                bool invalidArgument = false;

                if (string.Equals(arg.Key, StorageConnectionStringArgKey, StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(arg.Value))
                    {
                        invalidArgument = true;
                    }
                }
                else if (string.Equals(arg.Key, ScenarioStringArgKey, StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(arg.Key))
                    {
                        invalidArgument = true;
                    }
                }
                else
                {
                    invalidArgument = true;
                }

                if (invalidArgument)
                {
                    throw new ArgumentException("Invalid argument " + arg.Key);
                }
            }
        }

        private static IDictionary<string, string> ParseCommandLineArgs(string[] args)
        {
            const char ArgPrefix = '/';
            const char KeyValueSeparator = '=';
            
            Dictionary<string, string> parsedArgs = new Dictionary<string, string>();
            if (args != null)
            {
                foreach(string arg in args)
                {
                    int separatorIndex = arg.IndexOf(KeyValueSeparator);
                    if (separatorIndex < 0 || arg[0] != ArgPrefix)
                    {
                        throw new ArgumentException("Invalid argument " + arg);
                    }

                    // Start at 1 because of the / char
                    string key = arg.Substring(1, separatorIndex - 1);
                    string value = arg.Substring(separatorIndex + 1);

                    parsedArgs.Add(key, value);
                }
            }

            return parsedArgs;
        }
    }
}
