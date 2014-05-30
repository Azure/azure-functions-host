using Microsoft.Azure.Jobs.Host.Converters;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.Jobs.Host.Queues.Bindings
{
    internal class OutputConverter<TInput> : IObjectToTypeConverter<CloudQueue>
        where TInput : class
    {
        private readonly IConverter<TInput, CloudQueue> _innerConverter;

        public OutputConverter(IConverter<TInput, CloudQueue> innerConverter)
        {
            _innerConverter = innerConverter;
        }

        public bool TryConvert(object input, out CloudQueue output)
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
