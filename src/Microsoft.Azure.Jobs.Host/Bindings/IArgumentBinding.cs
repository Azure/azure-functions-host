namespace Microsoft.Azure.Jobs.Host.Bindings
{
    internal interface IArgumentBinding
    {
        IValueProvider Bind(object value);
    }
}
