namespace Microsoft.Azure.Jobs.Host.Converters
{
    internal interface IObjectToTypeConverter<T>
    {
        bool TryConvert(object input, out T output);
    }
}
