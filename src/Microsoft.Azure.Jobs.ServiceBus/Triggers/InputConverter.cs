using System;
using Microsoft.Azure.Jobs.Host.Converters;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.Jobs.ServiceBus.Triggers
{
    internal class InputConverter<TOutput> : ITypeToObjectConverter<BrokeredMessage>
    {
        private readonly IConverter<BrokeredMessage, TOutput> _innerConverter;

        public InputConverter(IConverter<BrokeredMessage, TOutput> innerConverter)
        {
            _innerConverter = innerConverter;
        }

        public bool CanConvert(Type outputType)
        {
            return typeof(TOutput) == outputType;
        }

        public object Convert(BrokeredMessage input)
        {
            return _innerConverter.Convert(input);
        }
    }
}
