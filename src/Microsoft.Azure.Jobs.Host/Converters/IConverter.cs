namespace Microsoft.Azure.Jobs.Host.Converters
{
    internal interface IConverter<TInput, TOutput>
    {
        TOutput Convert(TInput input);
    }
}
