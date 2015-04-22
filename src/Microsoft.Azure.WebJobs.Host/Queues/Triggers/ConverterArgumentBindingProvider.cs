// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;
using Microsoft.Azure.WebJobs.Host.Triggers;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Host.Queues.Triggers
{
    internal class ConverterArgumentBindingProvider<T> : IQueueTriggerArgumentBindingProvider
    {
        private readonly IConverter<IStorageQueueMessage, T> _converter;

        public ConverterArgumentBindingProvider(IConverter<IStorageQueueMessage, T> converter)
        {
            _converter = converter;
        }

        public ITriggerDataArgumentBinding<IStorageQueueMessage> TryCreate(ParameterInfo parameter)
        {
            if (parameter.ParameterType != typeof(T))
            {
                return null;
            }

            return new ConverterArgumentBinding(_converter);
        }

        internal class ConverterArgumentBinding : ITriggerDataArgumentBinding<IStorageQueueMessage>
        {
            private readonly IConverter<IStorageQueueMessage, T> _converter;

            public ConverterArgumentBinding(IConverter<IStorageQueueMessage, T> converter)
            {
                _converter = converter;
            }

            public Type ValueType
            {
                get { return typeof(T); }
            }

            public IReadOnlyDictionary<string, Type> BindingDataContract
            {
                get { return null; }
            }

            public Task<ITriggerData> BindAsync(IStorageQueueMessage value, ValueBindingContext context)
            {
                object converted = _converter.Convert(value);
                IValueProvider provider = new QueueMessageValueProvider(value, converted, typeof(T));
                return Task.FromResult<ITriggerData>(new TriggerData(provider, null));
            }
        }
    }
}
