// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Host.Queues.Bindings
{
    internal class CloudQueueMessageConverterFactory : IMessageConverterFactory<CloudQueueMessage>
    {
        public IConverter<CloudQueueMessage, IStorageQueueMessage> Create(IStorageQueue queue, Guid functionInstanceId)
        {
            return new CloudQueueMessageToStorageQueueMessageConverter();
        }
    }
}
