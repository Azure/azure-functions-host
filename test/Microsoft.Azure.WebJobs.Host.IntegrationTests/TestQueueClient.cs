// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Diagnostics;
using Microsoft.Azure.WebJobs.Host.Queues;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Queue;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Host.IntegrationTests
{
    public static class TestQueueClient
    {
        public static CloudQueue GetQueueReference(string queueName)
        {
            return GetQueueReference(TestStorage.GetAccount(), queueName);
        }

        public static CloudQueue GetQueueReference(CloudStorageAccount account, string queueName)
        {
            CloudQueueClient client = account.CreateCloudQueueClient();
            return client.GetQueueReference(queueName);
        }

        public static void DeleteQueue(string queueName)
        {
            DeleteQueue(TestStorage.GetAccount(), queueName);
        }

        [DebuggerNonUserCode]
        public static void DeleteQueue(CloudStorageAccount account, string queueName)
        {
            QueueClient.ValidateQueueName(queueName);

            var client = account.CreateCloudQueueClient();
            var queue = client.GetQueueReference(queueName);

            DeleteQueue(queue);
        }

        [DebuggerNonUserCode]
        public static void DeleteQueue(CloudQueue queue)
        {
            try
            {
                queue.Delete();
            }
            catch (StorageException)
            {
            }
        }
        
        public static T GetDeserializedMessage<T>(CloudQueue queue)
        {
            var msg = queue.GetMessage();
            if (msg != null)
            {
                string data = msg.AsString;
                queue.DeleteMessage(msg);

                T payload = JsonConvert.DeserializeObject<T>(data);
                return payload;
            }

            return default(T);
        }
    }
}
