// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Host.Queues.Bindings
{
    internal class StorageQueueArgumentBindingProvider : IQueueArgumentBindingProvider
    {
        public IArgumentBinding<IStorageQueue> TryCreate(ParameterInfo parameter)
        {
            if (parameter.ParameterType != typeof(IStorageQueue))
            {
                return null;
            }

            return new StorageQueueArgumentBinding();
        }

        private class StorageQueueArgumentBinding : IArgumentBinding<IStorageQueue>
        {
            public Type ValueType
            {
                get { return typeof(IStorageQueue); }
            }

            public Task<IValueProvider> BindAsync(IStorageQueue value, ValueBindingContext context)
            {
                IValueProvider provider = new QueueValueProvider(value, value, typeof(IStorageQueue));
                return Task.FromResult(provider);
            }
        }
    }
}
