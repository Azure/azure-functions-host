// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

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
