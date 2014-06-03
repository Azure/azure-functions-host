using System;

namespace Microsoft.Azure.Jobs.Host.Converters
{
    internal interface ITypeToObjectConverter<TInput>
    {
        bool CanConvert(Type outputType);

        object Convert(TInput input);
    }
}
