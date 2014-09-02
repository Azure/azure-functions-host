// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Host.TestCommon.AzureSdk
{
    public static class CloudQueueClientExtensions
    {
        public static void CreateQueueOrClearIfExists(this CloudQueueClient queueClient, string queueName)
        {
            CloudQueue queue = queueClient.GetQueueReference(queueName);

            bool wasCreatedNow = queue.CreateIfNotExists();
            if (!wasCreatedNow)
            {
                queue.Clear();
            }
        }

        public static void DeleteQueueIfExists(this CloudQueueClient queueClient, string queueName)
        {
            CloudQueue queue = queueClient.GetQueueReference(queueName);

            if (queue.Exists())
            {
                queue.Delete();
            }
        }
    }
}
