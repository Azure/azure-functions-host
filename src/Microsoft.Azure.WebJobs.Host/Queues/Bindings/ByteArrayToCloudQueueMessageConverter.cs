// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Host.Queues.Bindings
{
    internal class ByteArrayToCloudQueueMessageConverter : IConverter<byte[], CloudQueueMessage>
    {
        public CloudQueueMessage Convert(byte[] input)
        {
            if (input == null)
            {
                throw new InvalidOperationException("A queue message cannot contain a null byte array instance.");
            }

            return new CloudQueueMessage(input);
        }
    }
}
