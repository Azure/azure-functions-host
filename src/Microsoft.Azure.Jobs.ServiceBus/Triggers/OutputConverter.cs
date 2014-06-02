using Microsoft.Azure.Jobs.Host.Converters;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.Jobs.ServiceBus.Triggers
{
    internal class OutputConverter<TInput> : IObjectToTypeConverter<BrokeredMessage>
        where TInput : class
    {
        private readonly IConverter<TInput, BrokeredMessage> _innerConverter;

        public OutputConverter(IConverter<TInput, BrokeredMessage> innerConverter)
        {
            _innerConverter = innerConverter;
        }

        public bool TryConvert(object input, out BrokeredMessage output)
        {
            TInput typedInput = input as TInput;

            if (typedInput == null)
            {
                output = null;
                return false;
            }

            output = _innerConverter.Convert(typedInput);
            return true;
        }
    }
}
