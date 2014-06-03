namespace Microsoft.Azure.Jobs.Host.Converters
{
    internal interface IObjectToTypeConverter<TOutput>
    {
        bool TryConvert(object input, out TOutput output);
    }
}
