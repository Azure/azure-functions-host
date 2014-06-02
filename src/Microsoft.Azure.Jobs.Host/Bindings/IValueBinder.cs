namespace Microsoft.Azure.Jobs.Host.Bindings
{
    internal interface IValueBinder : IValueProvider
    {
        void SetValue(object value);
    }
}
