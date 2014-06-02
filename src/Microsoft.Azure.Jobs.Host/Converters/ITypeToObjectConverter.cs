using System;

namespace Microsoft.Azure.Jobs.Host.Converters
{
    internal interface ITypeToObjectConverter<T>
    {
        bool CanConvert(Type outputType);

        object Convert(T input);
    }
}
