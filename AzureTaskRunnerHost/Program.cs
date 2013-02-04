using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Executor;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using RunnerInterfaces;

namespace AzureTaskRunnerHost
{
    // Inputs for running the task. 
    public class ServiceInputs
    {
        public IFunctionUpdatedLogger Logger { get; set; }

        public FunctionInvokeRequest Instance { get; set; }
        public string LocalDir { get; set; } // Local dir (relative) that function dlls were xcopied too. 

        public LocalFunctionInstance GetLocalInstance()
        {
            return this.Instance.GetLocalFunctionInstance(this.LocalDir);
        }

        // For producing a ExecutionStatsAggregatorBridge
        // This is the storage account that the service is using (not the user's account that the function is in)
        public string AccountConnectionString { get; set; }
        public string QueueName { get; set; }

        public ExecutionStatsAggregatorBridge GetBridge()
        {
            var account = CloudStorageAccount.Parse(this.AccountConnectionString);
            CloudQueueClient client = account.CreateCloudQueueClient();
            var queue = client.GetQueueReference(this.QueueName);
            return new ExecutionStatsAggregatorBridge(queue);
        }
    }

    class Program
    {
        static void PrintDiagInfo()
        {
            // Diagnostic information for Azure Tasks
            Console.WriteLine("-------------------------------");
            Console.WriteLine("Hello!");

            string dir = Environment.CurrentDirectory;
            Console.WriteLine("Current dir: {0}", dir);

            foreach (var file in Directory.GetFiles(dir))
            {
                Console.WriteLine(Path.GetFileName(file));
            }
            Console.WriteLine("Dirs:");
            foreach (var file in Directory.GetDirectories(dir))
            {
                Console.WriteLine(file);
            }
        }

        // command-line entry point for Azure Task host. 
        // Doesnt need args since inputs are well-known files (named input.*.txt).
        static void Main(string[] args)
        {
            PrintDiagInfo();

            ServiceInputs inputs = GetLogger("input.logger.txt");
                        
            LocalFunctionInstance descr = inputs.GetLocalInstance();

            IAccountInfo account = new AccountInfo { AccountConnectionString = inputs.AccountConnectionString };
            var ctx = new FunctionExecutionContext
            {
                Account = account,
                Bridge = inputs.GetBridge(),
                Logger = inputs.Logger
            };

            Func<TextWriter, FunctionExecutionResult> fpInvokeFunc = (consoleOutput) =>
                {
                    var oldOutput = Console.Out;
                    try
                    {
                        Console.SetOut(consoleOutput);
                        return RunnerHost.Program.MainWorker(descr);
                        // worker will print exception information to console. 
                    }
                    finally
                    {                        
                        Console.SetOut(oldOutput);
                    }
                };

            ExecutionBase.Work(inputs.Instance, ctx, fpInvokeFunc);

            // $$$ When do we delete the WorkItems?
            // Now we're done. Results are saved off to blobs. Can delete the work item. 
            // Do we want to keep work item around for better AzureTask integration?           
        }

        private static ServiceInputs GetLogger(string filename)
        {
            string json = File.ReadAllText(filename);
            return JsonCustom.DeserializeObject<ServiceInputs>(json);
        }        
    }
}
