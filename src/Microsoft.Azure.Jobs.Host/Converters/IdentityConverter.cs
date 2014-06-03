namespace Microsoft.Azure.Jobs.Host.Converters
{
    internal class IdentityConverter<TValue> : IConverter<TValue, TValue>
    {
        public TValue Convert(TValue input)
        {
            return input;
        }
    }
}
