using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Executor;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using RunnerInterfaces;

namespace AzureTaskRunnerHost
{
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

            // Sharing code with ExecutorListener
            // Other functionality:
            // - heartbeat 
            // - check for abort
            // - 

            // Update logger, mark that function has begun executing

            ServiceInputs inputs = GetLogger("input.logger.txt");

            var bridge = inputs.GetBridge();

            IFunctionUpdatedLogger logger = inputs.Logger;
            var logItem = new ExecutionInstanceLogEntity();

            logItem.FunctionInstance = inputs.Instance;
            logItem.StartTime = DateTime.UtcNow;
            logger.Log(logItem);

            LocalFunctionInstance descr = inputs.GetLocalInstance();

            // main work happens here.
            Console.WriteLine("Got function! {0}", descr.MethodName);            

            FunctionExecutionResult result = RunnerHost.Program.MainWorker(descr);
                        
            // User errors returned via results.
            logItem.EndTime = DateTime.UtcNow;
            logItem.ExceptionType = result.ExceptionType;
            logItem.ExceptionMessage = result.ExceptionMessage;
            logger.Log(logItem);

            // Now we're done. Results are saved off to blobs. Can delete the work item. 
            // Do we want to keep work item around for better AzureTask integration?
            // !!! Some auto-delete option?

            // Invoke ExecutionStatsAggregatorBridge to queue a message back for the orchestrator. 
            bridge.EnqueueCompletedFunction(logItem);
            Console.WriteLine("Done, Queued to Bridge!");
        }

        private static ServiceInputs GetLogger(string filename)
        {
            string json = File.ReadAllText(filename);
            return JsonCustom.DeserializeObject<ServiceInputs>(json);
        }        
    }
}
