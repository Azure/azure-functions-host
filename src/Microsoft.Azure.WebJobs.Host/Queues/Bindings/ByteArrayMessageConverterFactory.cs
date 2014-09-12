// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Host.Queues.Bindings
{
    internal class ByteArrayMessageConverterFactory : IMessageConverterFactory<byte[]>
    {
        public IConverter<byte[], CloudQueueMessage> Create(Guid functionInstanceId)
        {
            return new ByteArrayToCloudQueueMessageConverter();
        }
    }
}
