using System;
using System.Collections.Generic;
using System.Data.Services.Client;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;

namespace RunnerInterfaces
{
    static partial class Utility
    {
        [DebuggerNonUserCode]
        public static void DeleteQueue(CloudStorageAccount account, string queueName)
        {
            ValidateQueueName(queueName);

            var client = account.CreateCloudQueueClient();
            var q = client.GetQueueReference(queueName);

            DeleteQueue(q);
        }

        [DebuggerNonUserCode]
        public static void DeleteQueue(CloudQueue queue)
        {
            try
            {
                queue.Delete();
            }
            catch (StorageClientException)
            {
            }
        }
    }
}