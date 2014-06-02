using Microsoft.Azure.Jobs.Host.Converters;

namespace Microsoft.Azure.Jobs.Host.Tables
{
    internal class EntityOutputConverter<TInput> : IObjectToTypeConverter<TableEntityContext>
        where TInput : class
    {
        private readonly IConverter<TInput, TableEntityContext> _innerConverter;

        public EntityOutputConverter(IConverter<TInput, TableEntityContext> innerConverter)
        {
            _innerConverter = innerConverter;
        }

        public bool TryConvert(object input, out TableEntityContext output)
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
