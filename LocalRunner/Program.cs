using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Executor;
using Orchestrator;
using RunnerInterfaces;

namespace LocalRunner
{
    class Program
    {
        public const string _localCache = @"c:\temp\cache";

        static void Main(string[] args)
        {
            RebuildIndex();

            Thread t = new Thread(OrchThread);
            t.Start();

            Thread t2 = new Thread(ExecutorThread);
            t2.Start();
            
            Thread.Sleep(-1);            
        }


        private static void RebuildIndex()
        {
            Console.WriteLine("Rebuilding function index...");
            var settings = new CloudIndexerSettings
            {
                Account = Secrets.GetAccount(),
                FunctionIndexTableName = Secrets.FunctionIndexTableName
            };
            Indexer i = new Indexer(settings);

                        
            var container = new CloudBlobDescriptor
            {
                // Dustin app
                AccountConnectionString = Secrets.AccountConnectionString,
                ContainerName = "daas-test-functions-compressor"
            };

            // Append to existing index
            // i.CleanFunctionIndex();
            i.IndexContainer(container, _localCache);

            Console.WriteLine("Done rebuilding function index.");
        }

        private static void ExecutorThread()
        {
            var settings = new ExecutionQueueSettings
            {
                Account = Secrets.GetAccount(),
                QueueName = Secrets.ExecutionQueueName
            };            
            var e = new Executor.ExecutorListener(_localCache, settings);
            e.Run(new ExecutionLogger());
        }

        private static void OrchThread()
        {
            var w = Services.GetOrchestrationWorker();
            w.Run();            
        }
    }

    class ExecutionLogger : EmptyExecutionLogger
    {
        public FunctionOutputLog GetLogStream(FunctionInstance f)
        {
            string dir = Path.Combine(Program._localCache, "output");
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            string file = Path.Combine(dir, f.Id.ToString() + ".txt");

            var output = new StreamWriter(file);
            return new FunctionOutputLog
            { 
                Output = output,
                CloseOutput = () => { output.Close(); },
            };
        }
    }
}
