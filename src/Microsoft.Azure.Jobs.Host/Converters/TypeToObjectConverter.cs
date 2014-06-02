using System;

namespace Microsoft.Azure.Jobs.Host.Converters
{
    internal class TypeToObjectConverter<TInput, TOutput> : ITypeToObjectConverter<TInput>
    {
        private readonly IConverter<TInput, TOutput> _innerConverter;

        public TypeToObjectConverter(IConverter<TInput, TOutput> innerConverter)
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
