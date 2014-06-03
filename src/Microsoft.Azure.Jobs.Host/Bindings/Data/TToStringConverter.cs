using Microsoft.Azure.Jobs.Host.Converters;

namespace Microsoft.Azure.Jobs.Host.Bindings.Data
{
    internal class TToStringConverter<TInput> : IConverter<TInput, string>
    {
        public string Convert(TInput input)
        {
            return input.ToString();
        }
    }
}
