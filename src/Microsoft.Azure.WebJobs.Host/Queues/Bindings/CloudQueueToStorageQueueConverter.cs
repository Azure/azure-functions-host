// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Host.Queues.Bindings
{
    internal class CloudQueueToStorageQueueConverter : IConverter<CloudQueue, IStorageQueue>
    {
        public IStorageQueue Convert(CloudQueue input)
        {
            if (input == null)
            {
                return null;
            }

            CloudQueueClient sdkClient = input.ServiceClient;
            Debug.Assert(sdkClient != null);
            IStorageQueueClient client = new StorageQueueClient(sdkClient);
            return new StorageQueue(client, input);
        }
    }
}
