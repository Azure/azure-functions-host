using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Executor;
using RunnerInterfaces;

namespace AzureTaskRunnerHost
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("-------------------------------");
            Console.WriteLine("Hello!");
            Console.WriteLine(args[0]);

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
                
#if true

            // Sharing code with ExecutorListener
            // Other functionality:
            // - heartbeat 
            // - check for abort
            // - 


            // !!! Update logger, mark that function has begun executing

            IFunctionUpdatedLogger logger = GetLogger(); 
            var logItem = new ExecutionInstanceLogEntity();
            logItem.StartTime = DateTime.UtcNow;
            logger.Log(logItem);


            // !!! Read via input.txt, already placed there by Azure Task infrastructure
            string filename = "input.txt"; // args[0]
            LocalFunctionInstance descr = ReadFromFile(filename);

            // main work happens here. !!!
            Console.WriteLine("Got function! {0}", descr.MethodName);
            
#if true
            FunctionExecutionResult result = RunnerHost.Program.MainWorker(descr);
            
            
            // User errors returned via results.
            logItem.EndTime = DateTime.UtcNow;
            logItem.ExceptionType = result.ExceptionType;
            logItem.ExceptionMessage = result.ExceptionMessage;
            logger.Log(logItem);


            // Now we're done. Results are saved off to blobs. Can delete the work item. 
            // !!! Some auto-delete option?
#endif
#endif
        }

        // $$$ Needs azure service credentials.
        private static IFunctionUpdatedLogger GetLogger()
        {
            return new NullLogger();
        }

        // !!! Remove this. 
        class NullLogger : IFunctionUpdatedLogger
        {
            public void Log(ExecutionInstanceLogEntity func)
            {
            }
        }

        private static LocalFunctionInstance ReadFromFile(string filename)
        {
            string content = File.ReadAllText(filename);
            return JsonCustom.DeserializeObject<LocalFunctionInstance>(content);
        }
    }
}
