// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.Azure.WebJobs.Host.Triggers;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.WebJobs.ServiceBus.Triggers
{
    internal class ConverterArgumentBindingProvider<T> : IQueueTriggerArgumentBindingProvider
    {
        private readonly IAsyncConverter<BrokeredMessage, T> _converter;

        public ConverterArgumentBindingProvider(IAsyncConverter<BrokeredMessage, T> converter)
        {
            _converter = converter;
        }

        public ITriggerDataArgumentBinding<BrokeredMessage> TryCreate(ParameterInfo parameter)
        {
            if (parameter.ParameterType != typeof(T))
            {
                return null;
            }

            return new ConverterArgumentBinding(_converter);
        }

        internal class ConverterArgumentBinding : ITriggerDataArgumentBinding<BrokeredMessage>
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

            public IReadOnlyDictionary<string, Type> BindingDataContract
            {
                get { return null; }
            }

            public async Task<ITriggerData> BindAsync(BrokeredMessage value, ValueBindingContext context)
            {
                BrokeredMessage clone = value.Clone();
                object converted = await _converter.ConvertAsync(value, context.CancellationToken);
                IValueProvider provider = await BrokeredMessageValueProvider.CreateAsync(clone, converted, typeof(T),
                    context.CancellationToken);
                return new TriggerData(provider, null);
            }
        }
    }
}
