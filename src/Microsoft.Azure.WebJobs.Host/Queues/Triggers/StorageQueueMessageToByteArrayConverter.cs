// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Host.Queues.Triggers
{
    internal class StorageQueueMessageToByteArrayConverter : IConverter<IStorageQueueMessage, byte[]>
    {
        public byte[] Convert(IStorageQueueMessage input)
        {
            return input.AsBytes;
        }
    }
}
