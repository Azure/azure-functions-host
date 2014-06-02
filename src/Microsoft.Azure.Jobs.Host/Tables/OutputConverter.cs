using Microsoft.Azure.Jobs.Host.Converters;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.Jobs.Host.Tables
{
    internal class OutputConverter<TInput> : IObjectToTypeConverter<CloudTable>
        where TInput : class
    {
        private readonly IConverter<TInput, CloudTable> _innerConverter;

        public OutputConverter(IConverter<TInput, CloudTable> innerConverter)
        {
            _innerConverter = innerConverter;
        }

        public bool TryConvert(object input, out CloudTable output)
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
