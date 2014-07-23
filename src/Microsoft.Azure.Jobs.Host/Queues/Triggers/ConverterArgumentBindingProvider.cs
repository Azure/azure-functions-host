// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Converters;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.Jobs.Host.Queues.Triggers
{
    internal class ConverterArgumentBindingProvider<T> : IQueueTriggerArgumentBindingProvider
    {
        private readonly IConverter<CloudQueueMessage, T> _converter;

        public ConverterArgumentBindingProvider(IConverter<CloudQueueMessage, T> converter)
        {
            _converter = converter;
        }

        public IArgumentBinding<CloudQueueMessage> TryCreate(ParameterInfo parameter)
        {
            if (parameter.ParameterType != typeof(T))
            {
                return null;
            }

            return new ConverterArgumentBinding(_converter);
        }

        internal class ConverterArgumentBinding : IArgumentBinding<CloudQueueMessage>
        {
            private readonly IConverter<CloudQueueMessage, T> _converter;

            public ConverterArgumentBinding(IConverter<CloudQueueMessage, T> converter)
            {
                _converter = converter;
            }

            public Type ValueType
            {
                get { return typeof(T); }
            }

            public Task<IValueProvider> BindAsync(CloudQueueMessage value, FunctionBindingContext context)
            {
                object converted = _converter.Convert(value);
                IValueProvider provider = new QueueMessageValueProvider(value, converted, typeof(T));
                return Task.FromResult(provider);
            }
        }
    }
}
