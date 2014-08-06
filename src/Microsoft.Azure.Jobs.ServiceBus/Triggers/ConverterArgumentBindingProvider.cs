// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Converters;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.Jobs.ServiceBus.Triggers
{
    internal class ConverterArgumentBindingProvider<T> : IQueueTriggerArgumentBindingProvider
    {
        private readonly IAsyncConverter<BrokeredMessage, T> _converter;

        public ConverterArgumentBindingProvider(IAsyncConverter<BrokeredMessage, T> converter)
        {
            _converter = converter;
        }

        public IArgumentBinding<BrokeredMessage> TryCreate(ParameterInfo parameter)
        {
            if (parameter.ParameterType != typeof(T))
            {
                return null;
            }

            return new ConverterArgumentBinding(_converter);
        }

        internal class ConverterArgumentBinding : IArgumentBinding<BrokeredMessage>
        {
            private readonly IAsyncConverter<BrokeredMessage, T> _converter;

            public ConverterArgumentBinding(IAsyncConverter<BrokeredMessage, T> converter)
            {
                _converter = converter;
            }

            public Type ValueType
            {
                get { return typeof(T); }
            }

            public async Task<IValueProvider> BindAsync(BrokeredMessage value, ValueBindingContext context)
            {
                BrokeredMessage clone = value.Clone();
                object converted = await _converter.ConvertAsync(value, context.CancellationToken);
                return await BrokeredMessageValueProvider.CreateAsync(clone, converted, typeof(T),
                    context.CancellationToken);
            }
        }
    }
}
