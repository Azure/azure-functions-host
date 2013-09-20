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
using Newtonsoft.Json;

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

        // Apply the callback funciton to each message in the queue. 
        // Delete message from queue once callback returns.
        // Return true if any messages are handled
        //[DebuggerNonUserCode]
        public static bool ApplyToQueue<T>(Action<T> callback, CloudQueue queue)
        {
            IEnumerable<CloudQueueMessage> msgs = null;
            try
            {
                msgs = queue.GetMessages(32); // 32 is max number

            }
            catch
            {
            }
            if (msgs == null)
            {
                return false;
            }

            int count = 0;
            foreach (var msg in msgs)
            {
                T payload;
                try
                {
                    payload = JsonCustom.DeserializeObject<T>(msg.AsString);
                }
                catch
                {
                    // Bad JSON serialization. Posionous. Just remove it.                    
                    queue.DeleteMessage(msg);
                    continue;
                }

                callback(payload);
                count++;

                try
                {
                    queue.DeleteMessage(msg);
                }
                catch { }
            }
            return count > 0;
        }
    }
}