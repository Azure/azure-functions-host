// This test covers the following performance metrics
// - Startup time for host (from the moment RunAndBlock is called, to the moment when the first function is invoked)
// - Execution time for a number of functions that are triggerd by queue messages

using System;
using System.Collections.Generic;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.Jobs.Host.FunctionChainingScenario
{
    public class Program
    {
        private const string ConnectionStringArgKey = "CS";

        public static int Main(string[] args)
        {
            try
            {
                IDictionary<string, string> commandLineArgs = ParseCommandLineArgs(args);
                ValidateCommandLineArguments(commandLineArgs);

                MainInternal(commandLineArgs[ConnectionStringArgKey]);
            }
            catch(Exception ex)
            {
                Console.WriteLine("Usage:");
                Console.WriteLine("Jobs.Host.PerfTest1.exe /CS=<storage connection string>");
                Console.WriteLine();
                Console.WriteLine("--- Exception details ---");
                Console.WriteLine(ex.ToString());

                return -1;
            }

            return 0;
        }

        private static void MainInternal(string connectionString)
        {
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(connectionString);

            CloudQueueClient queueClient = storageAccount.CreateCloudQueueClient();
            CreateTestQueues(queueClient);

            try
            {
                CloudQueue firstQueue = queueClient.GetQueueReference(PerfTest.FirstQueueName);
                firstQueue.AddMessage(new CloudQueueMessage("Test"));

                PerfTest.Run(connectionString);
            }
            finally
            {
                DeleteTestQueues(queueClient);
            }
        }

        private static void CreateTestQueues(CloudQueueClient queueClient)
        {
            CreateQueueOrClearIfExists(PerfTest.FirstQueueName, queueClient);
            CreateQueueOrClearIfExists(PerfTest.LastQueueName, queueClient);

            // Create the queues to pass messages around
            for (int i = 1; i <= PerfTest.NumberOfGeneratedMethods; i++)
            {
                CreateQueueOrClearIfExists(PerfTest.PerfQueuePrefix + i, queueClient);
            }
        }

        private static void DeleteTestQueues(CloudQueueClient queueClient)
        {
            DeleteQueueIfExists(PerfTest.FirstQueueName, queueClient);
            DeleteQueueIfExists(PerfTest.LastQueueName, queueClient);

            // Create the queues to pass messages around
            for (int i = 1; i <= PerfTest.NumberOfGeneratedMethods; i++)
            {
                DeleteQueueIfExists(PerfTest.PerfQueuePrefix + i, queueClient);
            }
        }

        private static void CreateQueueOrClearIfExists(string queueName, CloudQueueClient queueClient)
        {
            CloudQueue queue = queueClient.GetQueueReference(queueName);

            bool wasCreatedNow = queue.CreateIfNotExists();
            if (!wasCreatedNow)
            {
                queue.Clear();
            }
        }

        private static void DeleteQueueIfExists(string queueName, CloudQueueClient queueClient)
        {
            CloudQueue queue = queueClient.GetQueueReference(queueName);

            if (queue.Exists())
            {
                queue.Delete();
            }
        }

        private static void ValidateCommandLineArguments(IDictionary<string, string> args)
        {
            foreach(var arg in args)
            {
                bool invalidArgument = false;

                if (string.Equals(arg.Key, ConnectionStringArgKey, StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(arg.Value))
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
