namespace Microsoft.Azure.Jobs.Host.Converters
{
    internal class IdentityConverter<T> : IConverter<T, T>
    {
        public T Convert(T input)
        {
            return input;
        }
    }
}
