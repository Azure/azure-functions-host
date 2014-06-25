using System;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Converters;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.Jobs.ServiceBus.Triggers
{
    internal class ConverterArgumentBindingProvider<T> : IQueueTriggerArgumentBindingProvider
    {
        private readonly IConverter<BrokeredMessage, T> _converter;

        public ConverterArgumentBindingProvider(IConverter<BrokeredMessage, T> converter)
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
            private readonly IConverter<BrokeredMessage, T> _converter;

            public ConverterArgumentBinding(IConverter<BrokeredMessage, T> converter)
            {
                _converter = converter;
            }

            public Type ValueType
            {
                get { return typeof(T); }
            }

            public IValueProvider Bind(BrokeredMessage value, FunctionBindingContext context)
            {
                BrokeredMessage clone = value.Clone();
                object converted = _converter.Convert(value);
                return new BrokeredMessageValueProvider(clone, converted, typeof(T));
            }
        }
    }
}
