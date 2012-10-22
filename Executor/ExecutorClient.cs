using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using Newtonsoft.Json;
using RunnerInterfaces;

namespace Executor
{
    public class ExecutorClient
    {
        // Queue a message to run the given function instance
        // This just queus it and does not do any logging. 
        public static void Queue(ExecutionQueueSettings queueSettings, FunctionInstance instance)
        {
            // Queue tutorial here:
            //   http://www.developerfusion.com/article/120197/using-the-queuing-service-in-windows-azure/ 

            instance.SchemaNumber = FunctionInstance.CurrentSchema;
            
            // Caller should have set ID. 
            
            var queue = queueSettings.GetQueue();

            string json = JsonCustom.SerializeObject(instance);
            var msg = new CloudQueueMessage(json);
            queue.AddMessage(msg);
        }
    }
}
