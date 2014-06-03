using Microsoft.Azure.Jobs.Host.Converters;

namespace Microsoft.Azure.Jobs.Host.Bindings
{
    internal class StringToTConverter<TOutput> : IConverter<string, TOutput>
    {
        public TOutput Convert(string input)
        {
            return (TOutput)ObjectBinderHelpers.BindFromString(input, typeof(TOutput));
        }
    }
}
