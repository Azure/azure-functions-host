// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Host.Queues.Bindings
{
    internal class UserTypeMessageConverterFactory<TInput> : IMessageConverterFactory<TInput>
    {
        public IConverter<TInput, IStorageQueueMessage> Create(IStorageQueue queue, Guid functionInstanceId)
        {
            return new UserTypeToStorageQueueMessageConverter<TInput>(queue, functionInstanceId);
        }
    }
}
