using System;
using Microsoft.Azure.Jobs.Host.Converters;

namespace Microsoft.Azure.Jobs.Host.Bindings.Data
{
    internal class InputConverter<TInput, TOutput> : ITypeToObjectConverter<TInput>
    {
        private readonly IConverter<TInput, TOutput> _innerConverter;

        public InputConverter(IConverter<TInput, TOutput> innerConverter)
        {
            _innerConverter = innerConverter;
        }

        public bool CanConvert(Type outputType)
        {
            return typeof(TOutput) == outputType;
        }

        public object Convert(TInput input)
        {
            return _innerConverter.Convert(input);
        }
    }
}
