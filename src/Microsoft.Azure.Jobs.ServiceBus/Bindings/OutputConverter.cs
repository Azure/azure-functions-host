using Microsoft.Azure.Jobs.Host.Converters;

namespace Microsoft.Azure.Jobs.ServiceBus.Bindings
{
    internal class OutputConverter<TInput> : IObjectToTypeConverter<ServiceBusEntity>
        where TInput : class
    {
        private readonly IConverter<TInput, ServiceBusEntity> _innerConverter;

        public OutputConverter(IConverter<TInput, ServiceBusEntity> innerConverter)
        {
            _innerConverter = innerConverter;
        }

        public bool TryConvert(object input, out ServiceBusEntity output)
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
