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
    internal class ExecutorClient
    {
        // Queue a message to run the given function instance
        // This just queus it and does not do any logging. 
        public static void Queue(CloudQueue queue, FunctionInvokeRequest instance)
        {
            // Queue tutorial here:
            //   http://www.developerfusion.com/article/120197/using-the-queuing-service-in-windows-azure/ 

           
            // Caller should have set ID.             
            string json = JsonCustom.SerializeObject(instance);
            var msg = new CloudQueueMessage(json);
            queue.AddMessage(msg);
        }
    }
}
