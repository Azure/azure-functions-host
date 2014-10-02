// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Host.Queues.Bindings
{
    internal class CloudQueueMessageToStorageQueueMessageConverter : IConverter<CloudQueueMessage, IStorageQueueMessage>
    {
        public IStorageQueueMessage Convert(CloudQueueMessage input)
        {
            if (input == null)
            {
                return null;
            }

            return new StorageQueueMessage(input);
        }
    }
}
