using System.Collections.Generic;

namespace Microsoft.Azure.Jobs.Host.Converters
{
    internal class CompositeObjectToTypeConverter<T> : IObjectToTypeConverter<T>
    {
        private readonly IEnumerable<IObjectToTypeConverter<T>> _converters;

        public CompositeObjectToTypeConverter(IEnumerable<IObjectToTypeConverter<T>> converters)
        {
            _converters = converters;
        }

        public CompositeObjectToTypeConverter(params IObjectToTypeConverter<T>[] converters)
            : this((IEnumerable<IObjectToTypeConverter<T>>)converters)
        {
        }

        public bool TryConvert(object value, out T converted)
        {
            foreach (IObjectToTypeConverter<T> converter in _converters)
            {
                T possibleConverted;

                if (converter.TryConvert(value, out possibleConverted))
                {
                    converted = possibleConverted;
                    return true;
                }
            }

            converted = default(T);
            return false;
        }
    }
}
